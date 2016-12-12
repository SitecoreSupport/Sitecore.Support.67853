using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using Sitecore.Abstractions;
using Sitecore.Configuration;
using Sitecore.Data;
using Sitecore.Diagnostics;
using Sitecore.Form.Core;
using Sitecore.Form.Core.Data;
using Sitecore.Form.Core.Pipelines.FormSubmit;
using Sitecore.Sites;
using Sitecore.WFFM.Abstractions.Actions;
using Sitecore.WFFM.Abstractions.Data;
using Sitecore.WFFM.Abstractions.Dependencies;
using Sitecore.WFFM.Abstractions.Shared;
using Sitecore.WFFM.Abstractions.Utils;
using Sitecore.WFFM.Abstractions.Wrappers;
using ISettings = Sitecore.WFFM.Abstractions.Shared.ISettings;

namespace Sitecore.Support.Forms.Core.Handlers
{
  [DependencyPath("wffm/supportFormDataHandler")]
  public class FormDataHandler
  {
    // Fields
    private readonly IActionExecutor actionExecutor;
    private readonly IAnalyticsTracker analyticsTracker;
    private readonly ICorePipeline corePipeline;
    private readonly IEventManager eventManager;
    private readonly IFormContext formContext;
    private readonly IItemRepository itemRepository;
    private readonly ILogger logger;
    private readonly ISettings settings;
    private readonly ISitecoreContextWrapper sitecoreContextWrapper;
    private readonly IWebUtil webUtil;

    public static long PostedFilesLimit => StringUtil.ParseSizeString(Settings.GetSetting("WFM.PostedFilesLimit", "1024KB"));

    // Methods
    public FormDataHandler(IActionExecutor actionExecutor, ILogger logger, ISitecoreContextWrapper sitecoreContextWrapper, ISettings settings, IAnalyticsTracker analyticsTracker, IItemRepository itemRepository, IEventManager eventManager, ICorePipeline corePipeline, IWebUtil webUtil, IFormContext formContext)
    {
      Assert.ArgumentNotNull(actionExecutor, "actionExecutorParam");
      Assert.ArgumentNotNull(logger, "logger");
      Assert.ArgumentNotNull(sitecoreContextWrapper, "sitecoreContextWrapper");
      Assert.ArgumentNotNull(settings, "settings");
      Assert.ArgumentNotNull(analyticsTracker, "analyticsTracker");
      Assert.ArgumentNotNull(itemRepository, "itemRepository");
      Assert.ArgumentNotNull(eventManager, "eventManagerWrapper");
      Assert.ArgumentNotNull(corePipeline, "corePipelineWrapper");
      Assert.ArgumentNotNull(webUtil, "webUtil");
      Assert.ArgumentNotNull(formContext, "formContext");
      this.actionExecutor = actionExecutor;
      this.logger = logger;
      this.sitecoreContextWrapper = sitecoreContextWrapper;
      this.settings = settings;
      this.analyticsTracker = analyticsTracker;
      this.itemRepository = itemRepository;
      this.eventManager = eventManager;
      this.corePipeline = corePipeline;
      this.webUtil = webUtil;
      this.formContext = formContext;
    }

    private void ExecuteSaveActions(ID formId, ControlResult[] fields, IActionDefinition[] actions, IActionExecutor actionExecutorParam)
    {
      if (((sitecoreContextWrapper.ContextSiteDisplayMode != DisplayMode.Normal) && (sitecoreContextWrapper.ContextSiteDisplayMode != DisplayMode.Preview)) || (webUtil.GetQueryString("sc_debug", null) != null)) return;

      if (settings.IsRemoteActions)
      {
        var event3 = new WffmActionEvent
        {
          FormID = formId,
          SessionIDGuid = analyticsTracker.SessionId.Guid
        };

        Func<IActionDefinition, bool> predicate = delegate (IActionDefinition s)
        {
          var item = itemRepository.CreateAction(s.ActionID);
          return (item != null) && !item.IsClientAction;
        };

        event3.Actions = actions.Where(predicate).ToArray();
        event3.Fields = GetSerializableControlResults(fields).ToArray();
        event3.UserName = settings.RemoteActionsUserName;
        event3.Password = settings.RemoteActionsUserPassword;

        var event2 = event3;
        eventManager.QueueEvent(event2);

        Func<IActionDefinition, bool> func2 = delegate (IActionDefinition s)
        {
          var item = itemRepository.CreateAction(s.ActionID);
          return (item != null) && item.IsClientAction;
        };

        var result = actionExecutorParam.ExecuteSaving(formId, fields, actions.Where(func2).ToArray(), true, analyticsTracker.SessionId);
        if (result.Failures.Any())
        {
          formContext.Failures.AddRange(result.Failures);
        }
      }
      else
      {
        var result2 = actionExecutorParam.ExecuteSaving(formId, fields, actions, false, analyticsTracker.SessionId);
        if (result2.Failures.Any())
        {
          formContext.Failures.AddRange(result2.Failures);
        }
      }
    }

    private IEnumerable<ControlResult> GetSerializableControlResults(IEnumerable<ControlResult> fields)
    {
      var resultArray = fields as ControlResult[] ?? fields.ToArray();

      Assert.ArgumentCondition(GetUploadedSizeOfAllFiles(resultArray) < PostedFilesLimit, "Posted files size", "Posted files size exceeds limit");

      return (from f in resultArray
              select new ControlResult
              {
                FieldID = f.FieldID,
                FieldName = f.FieldName,
                Value = GetSerializedValue(f.Value),
                FieldType = (f.Value == null) ? typeof(object).ToString() : f.Value.GetType().AssemblyQualifiedName,
                Parameters = f.Parameters,
                Secure = f.Secure,
                AdaptForAnalyticsTag = f.AdaptForAnalyticsTag
              });
    }

    private object GetSerializedValue(object value)
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

    private long GetUploadedSizeOfAllFiles(IEnumerable<ControlResult> fields)
    {
      return fields.Select(result => result.Value as PostedFile).Where(file => file?.Data != null).Aggregate(0L, (current, file) => current + file.Data.Length);
    }

    public void ProcessForm(ID formId, ControlResult[] fields, IActionDefinition[] actions)
    {
      ProcessFormImplementation(formId, fields, actions, actionExecutor);
    }

    private void ProcessFormImplementation(ID formId, ControlResult[] fields, IActionDefinition[] actions, IActionExecutor actionExecutorParam)
    {
      Assert.ArgumentNotNull(formId, "formID");
      Assert.ArgumentNotNull(fields, "fields");
      Assert.ArgumentNotNull(actions, "actions");
      formContext.Failures = new List<ExecuteResult.Failure>();

      if (ID.IsNullOrEmpty(formId)) return;
      actionExecutorParam.ExecuteChecking(formId, fields, actions);

      try
      {
        ExecuteSaveActions(formId, fields, actions, actionExecutorParam);
        actionExecutorParam.ExecuteSystemAction(formId, fields);
      }
      catch (Exception exception)
      {
        logger.Warn(exception.Message, exception, this);
        var item = new ExecuteResult.Failure
        {
          ErrorMessage = exception.Message,
          FailedAction = ID.Null.ToString(),
          IsCustom = false
        };
        formContext.Failures.Add(item);
      }

      if (!formContext.Failures.Any()) return;

      var args = new SubmittedFormFailuresArgs(formId, formContext.Failures)
      {
        Database = settings.ContextDatabaseName
      };
      try
      {
        corePipeline.Run("errorSubmit", args);
      }
      catch (Exception exception2)
      {
        logger.Warn(exception2.Message, exception2, this);
      }
      throw new FormSubmitException(args.Failures);
    }
  }
}


