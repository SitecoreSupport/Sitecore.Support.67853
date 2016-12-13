using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using Sitecore.Data;
using Sitecore.Diagnostics;
using Sitecore.Eventing;
using Sitecore.Form.Core;
using Sitecore.Form.Core.Configuration;
using Sitecore.Form.Core.Controls.Data;
using Sitecore.Form.Core.Data;
using Sitecore.Form.Core.Media;
using Sitecore.Form.Core.Pipelines.FormSubmit;
using Sitecore.Form.Core.Submit;
using Sitecore.Pipelines;
using Sitecore.Sites;
using Sitecore.Support.Form.Core.Submit;
using Sitecore.Web;
using Sitecore.WFFM.Analytics;
using Settings = Sitecore.Configuration.Settings;
using SubmitActionManager = Sitecore.Support.Form.Core.Submit.SubmitActionManager;

namespace Sitecore.Support.Form.Core
{
  public class FormDataHandler
  {
    public static long PostedFilesLimit => StringUtil.ParseSizeString(Settings.GetSetting("WFM.PostedFilesLimit", "1024KB"));

    private static void ExecuteSaveActions(ID formId, ControlResult[] fields, ActionDefinition[] actions)
    {
      if (((Context.Site.DisplayMode != DisplayMode.Normal) && (Context.Site.DisplayMode != DisplayMode.Preview)) ||
          (WebUtil.GetQueryString("sc_debug", null) != null)) return;

      if (Sitecore.Form.Core.Configuration.Settings.IsRemoteActions)
      {
        var event2 = new WffmActionEvent
        {
          FormID = formId,
          SessionIDGuid = AnalyticsTracker.SessionId.Guid,
          Actions = actions.Where(delegate(ActionDefinition s)
          {
            var item = StaticSettings.ContextDatabase.GetItem(s.ActionID);
            return (item != null) && !new ActionItem(item).IsClientAction;
          }).ToArray(),
          Fields = GetSerializableControlResults(fields).ToArray(),
          UserName = Sitecore.Form.Core.Configuration.Settings.RemoteActionsUserName,
          Password = Sitecore.Form.Core.Configuration.Settings.RemoteActionsUserPassword
        };

        EventManager.QueueEvent(event2);

        var result = SubmitActionManager.ExecuteSaving(formId, fields, actions.Where(delegate(ActionDefinition s)
        {
          var item = StaticSettings.ContextDatabase.GetItem(s.ActionID);
          return (item != null) && new ActionItem(item).IsClientAction;
        }).ToArray(), true, AnalyticsTracker.SessionId);

        if (result.Failures.Length > 0) FormContext.Failures.AddRange(result.Failures);
      }
      else
      {
        var result2 = SubmitActionManager.ExecuteSaving(formId, fields, actions, false, AnalyticsTracker.SessionId);
        if (result2.Failures.Length > 0)FormContext.Failures.AddRange(result2.Failures);
      }
    }

    private static IEnumerable<ControlResult> GetSerializableControlResults(IEnumerable<ControlResult> fields)
    {
      var controlResults = fields as ControlResult[] ?? fields.ToArray();
      Assert.ArgumentCondition(GetUploadedSizeOfAllFiles(controlResults) < PostedFilesLimit, "Posted files size", "Posted files size exceeds limit");

      return controlResults.Select(delegate(ControlResult f)
      {
        var result = new ControlResult
        {
          FieldID = f.FieldID,
          FieldName = f.FieldName,
          Value = GetSerializedValue(f.Value),
          FieldType = f.Value?.GetType().ToString() ?? typeof(object).ToString(),
          Parameters = f.Parameters
        };
        return result;
      });
    }

    private static object GetSerializedValue(object value)
    {
      var file = value as PostedFile;

      if (file != null)
      {
        var file2 = new PostedFile
        {
          Data = file.Data,
          Destination = file.Destination,
          FileName = file.FileName
        };
        value = file2;
      }

      var sb = new StringBuilder();

      using (TextWriter writer = new StringWriter(sb))
      {
        if (value != null) new XmlSerializer(value.GetType()).Serialize(writer, value);
        value = sb.ToString();
      }
      return value;
    }

    private static long GetUploadedSizeOfAllFiles(IEnumerable<ControlResult> fields)
    {
      return fields.Select(result => result.Value as PostedFile).Where(file => file?.Data != null).Aggregate(0L, (current, file) => current + file.Data.Length);
    }

    public static void ProcessData(ID formID, ControlResult[] fields, ActionDefinition[] actions)
    {
      Assert.ArgumentNotNull(formID, "formID");
      Assert.ArgumentNotNull(fields, "fields");
      Assert.ArgumentNotNull(actions, "actions");
      FormContext.Failures = new List<ExecuteResult.Failure>();

      if (ID.IsNullOrEmpty(formID)) return;
      SubmitActionManager.ExecuteChecking(formID, fields, actions);

      try
      {
        ExecuteSaveActions(formID, fields, actions);
        SubmitActionManager.ExecuteSystemAction(formID, fields);
      }
      catch (Exception exception)
      {
        Log.Warn(exception.Message, exception, new object());

        var item = new ExecuteResult.Failure
        {
          ErrorMessage = exception.Message,
          FailedAction = ID.Null.ToString(),
          IsCustom = false
        };

        FormContext.Failures.Add(item);
      }

      if (FormContext.Failures.Count <= 0) return;

      var args2 = new SubmittedFormFailuresArgs(formID, FormContext.Failures)
      {
        Database = StaticSettings.ContextDatabase.Name
      };
      try
      {
        CorePipeline.Run("errorSubmit", args2);
      }
      catch
      {
        // ignored
      }

      throw new FormSubmitException(args2.Failures);
    }
  }
}