using System;
using System.Collections.Generic;
using System.Linq;
using Sitecore.Analytics;
using Sitecore.Configuration;
using Sitecore.Data;
using Sitecore.Data.Items;
using Sitecore.Diagnostics;
using Sitecore.Events;
using Sitecore.Form.Core.Client.Data.Submit;
using Sitecore.Form.Core.Client.Submit;
using Sitecore.Form.Core.Configuration;
using Sitecore.Form.Core.ContentEditor.Data;
using Sitecore.Form.Core.Controls.Data;
using Sitecore.Form.Core.Data;
using Sitecore.Form.Core.Pipelines.FormSubmit;
using Sitecore.Form.Core.Submit;
using Sitecore.Form.Core.Utility;
using Sitecore.Form.Submit;
using Sitecore.Forms.Core.Data;
using Sitecore.Pipelines;
using Sitecore.Reflection;
using Sitecore.WFFM.Analytics;
using Sitecore.WFFM.Analytics.Model;
using Sitecore.WFFM.Analytics.Providers;
using Sitecore.WFFM.Core.Extensions;
using Sitecore.WFFM.Core.Resources;
using IDs = Sitecore.WFFM.Analytics.Core.IDs;

namespace Sitecore.Support.Form.Core.Submit
{
  public class SubmitActionManager
  {
    private static List<AdaptedControlResult> AdaptResult(IEnumerable<ControlResult> list, bool simpleAdapt) =>
      (from result in list select new AdaptedControlResult(result, simpleAdapt)).ToList();

    public static void Execute(ID formID, ControlResult[] list, ActionDefinition[] actions)
    {
      ExecuteSaving(formID, list, actions, false, null);
    }

    internal static void ExecuteChecking(ID formID, ControlResult[] results, ActionDefinition[] actions)
    {
      ActionDefinition definition = null;
      var item = new FormItem(StaticSettings.ContextDatabase.GetItem(formID));
      try
      {
        object[] objArray1 = {formID, results};
        RaiseEvent("forms:check", objArray1);
        foreach (var definition2 in actions)
        {
          definition = definition2;
          var item2 = ActionItem.GetAction(definition2.ActionID);
          if (item2 != null)
          {
            if (item2.ActionType == ActionType.Check)
            {
              var action = (ICheckAction) ReflectionUtil.CreateObject(item2.Assembly, item2.Class, new object[0]);
              ReflectionUtils.SetXmlProperties(action, definition2.Paramaters, true);
              ReflectionUtils.SetXmlProperties(action, item2.GlobalParameters, true);
              if (action is BaseAction)
              {
                var action2 = action as BaseAction;
                action2.GetType().GetProperty("UniqueKey").SetValue(action2, definition2.UniqueKey);
                action2.GetType().GetProperty("ActionID").SetValue(action2, item2.ID);
                action2.Context.FormItem = item;
              }
              action.Execute(formID, results);
            }
          }
          else
          {
            Log.Warn($"Web Forms for Marketers : The '{definition2.ActionID}' action does not exist", new object());
          }
        }
      }
      catch (Exception exception)
      {
        if (definition == null)
          throw;
        var failureMessage = definition.GetFailureMessage(false, ID.Null);
        if (string.IsNullOrEmpty(failureMessage))
          failureMessage = exception.Message;
        var args2 = new CheckFailedArgs(formID, definition.ActionID, results, exception)
        {
          ErrorMessage = failureMessage
        };
        CorePipeline.Run("errorCheck", args2);
        if (item.IsDropoutTrackingEnabled)
          AnalyticsTracker.TriggerEvent(IDs.FormCheckActionErrorId, "Form Check Action Error", formID,
            args2.ErrorMessage, definition.GetTitle());
        throw new ValidatorException(args2.ErrorMessage);
      }
    }

    internal static ExecuteResult ExecuteSaving(ID formID, ControlResult[] list, ActionDefinition[] actions,
      bool simpleAdapt, ID sessionID)
    {
      AdaptedResultList fields = AdaptResult(list, simpleAdapt);
      if (actions.Length == 0)
        Log.Warn(string.Format(ResourceManager.GetString("NOT_DEFINED_ACTIONS"), formID), actions);
      object[] objArray1 = {formID, fields};
      RaiseEvent("forms:save", objArray1);
      var list3 = new List<ExecuteResult.Failure>();
      try
      {
        SaveFormToDatabase(formID, fields);
      }
      catch (Exception exception)
      {
        Log.Warn(exception.Message, exception, exception);
      }
      var context = new CallContext();
      foreach (var definition in actions)
      {
        var definition2 = definition;
        try
        {
          var item = ActionItem.GetAction(definition.ActionID);
          if (item != null)
          {
            if (item.ActionType == ActionType.Save)
            {
              var obj2 = ReflectionUtil.CreateObject(item.Assembly, item.Class, new object[0]);
              ReflectionUtils.SetXmlProperties(obj2, definition.Paramaters, true);
              ReflectionUtils.SetXmlProperties(obj2, item.GlobalParameters, true);
              if (obj2 is ISaveAction)
              {
                if (obj2 is BaseSaveAction)
                {
                  var action = (BaseSaveAction) obj2;
                  action.GetType().GetProperty("UniqueKey").SetValue(action, definition.UniqueKey);
                  action.GetType().GetProperty("ActionID").SetValue(action, item.ID);
                  action.GetType().GetProperty("Context").SetValue(action, context);
                }
                object[] objArray2 = {sessionID};
                ((ISaveAction) obj2).Execute(formID, fields, objArray2);
              }
            }
          }
          else
          {
            Log.Warn($"Web Forms for Marketers : The '{definition.ActionID}' action does not exist", new object());
          }
        }
        catch (Exception exception2)
        {
          Log.Warn(exception2.Message, exception2, exception2);
          var failureMessage = definition2.GetFailureMessage();
          var failure2 = new ExecuteResult.Failure();
          failure2.IsCustom = !string.IsNullOrEmpty(failureMessage);
          var failure = failure2;
          var args2 = new SaveFailedArgs(formID, fields, definition2.ActionID, exception2)
          {
            ErrorMessage = failure.IsCustom ? failureMessage : exception2.Message
          };
          CorePipeline.Run("errorSave", args2);
          failure.ApiErrorMessage = exception2.Message;
          failure.ErrorMessage = args2.ErrorMessage;
          failure.FailedAction = definition2.ActionID;
          failure.StackTrace = exception2.StackTrace;
          list3.Add(failure);
        }
      }
      var result = new ExecuteResult();
      result.Failures = list3.ToArray();
      return result;
    }

    internal static void ExecuteSystemAction(ID formID, ControlResult[] list)
    {
      var item = StaticSettings.ContextDatabase.GetItem(FormIDs.SystemActionsRootID);
      if ((item != null) && item.HasChildren)
      {
        AdaptedResultList list2 = AdaptResult(list, true);
        string query = $".//*[@@templateid = '{FormIDs.ActionTemplateID}']";
        foreach (var item2 in item.Axes.SelectItems(query))
        {
          var item3 = new ActionItem(item2);
          try
          {
            var action = (ISaveAction) ReflectionUtil.CreateObject(item3.Assembly, item3.Class, new object[0]);
            ReflectionUtils.SetXmlProperties(action, item3.GlobalParameters, true);
            object[] objArray1 = {AnalyticsTracker.SessionId};
            action.Execute(formID, list2, objArray1);
          }
          catch (Exception)
          {
            var failure = new ExecuteResult.Failure();
            failure.FailedAction = item3.Name;
            FormContext.Failures.Add(failure);
          }
        }
      }
    }

    public static ActionItem GetAcitonByUniqId(FormItem form, string uniqid, bool saveAction)
    {
      Assert.ArgumentNotNull(form, "form");
      var definition = ListDefinition.Parse(saveAction ? form.SaveActions : form.CheckActions);
      if (definition.Groups.Count > 0)
      {
        var definition2 = definition.Groups[0].ListItems.FirstOrDefault(i => i.Unicid == uniqid);
        if ((definition2 != null) && !string.IsNullOrEmpty(definition2.ItemID))
        {
          var item = form.Database.GetItem(definition2.ItemID);
          if (item != null)
            return new ActionItem(item);
        }
      }
      return null;
    }

    public static IEnumerable<ActionItem> GetActions(Item form) =>
    (from item in
      ListDefinition.Parse(form[Sitecore.Form.Core.Configuration.FieldIDs.SaveActionsID]).Groups[0].ListItems
      select form.Database.GetItem(item.ItemID)
      into command
      where command != null
      select new ActionItem(command)).ToList();

    public static IEnumerable<ActionItem> GetCheckActions(Item form) =>
      from s in GetActions(form)
      where s.ActionType == ActionType.Check
      select s;

    public static IEnumerable<ActionItem> GetSaveActions(Item form) =>
      from s in GetActions(form)
      where s.ActionType == ActionType.Save
      select s;

    private static void RaiseEvent(string eventName, params object[] args)
    {
      try
      {
        Event.RaiseEvent(eventName, args);
      }
      catch
      {
      }
    }

    public static void SaveFormToDatabase(ID formid, AdaptedResultList fields)
    {
      if (!Warn.IsNull(Tracker.Current, " Tracker.Current") && (StaticSettings.ContextDatabase.GetItem(formid) != null))
      {
        var dataArray = (from f in fields
          select new FieldData
          {
            FieldId = new Guid(f.FieldID),
            FieldName = f.FieldName,
            Data = f.Secure ? string.Empty : f.Parameters,
            Value = f.Secure ? string.Empty : f.Value
          }).ToArray<IFieldData>();
        var form = new FormData
        {
          ContactId =
            Warn.IsNull(Tracker.Current.Contact, "Tracker.Current.Contact")
              ? Guid.Empty
              : Tracker.Current.Contact.ContactId,
          FormID = formid.Guid,
          InteractionId =
            Warn.IsNull(Tracker.Current.Interaction, " Tracker.Current.Interaction")
              ? Guid.Empty
              : Tracker.Current.Interaction.InteractionId,
          Fields = dataArray,
          Timestamp = DateTime.UtcNow
        };
        (Factory.CreateObject("wffm/formsDataProvider", true) as IWfmDataProvider).InsertForm(form);
      }
    }
  }
}