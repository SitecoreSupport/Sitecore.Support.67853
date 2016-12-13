using System;
using System.Linq;
using Sitecore.Diagnostics;
using Sitecore.Form.Core.Configuration;
using Sitecore.Form.Core.Data;
using Sitecore.Form.Core.Submit;
using Sitecore.Forms.Core.Data;
using Sitecore.Forms.Mvc.Data.Analytics;
using Sitecore.Forms.Mvc.Events;
using Sitecore.Forms.Mvc.Interfaces;
using Sitecore.Forms.Mvc.Models;
using Sitecore.Forms.Mvc.Models.Fields;
using Sitecore.Forms.Mvc.Processors;
using Sitecore.Mvc.Presentation;
using Sitecore.StringExtensions;
using Sitecore.Support.Form.Core;
using Sitecore.WFFM.Analytics;
using Sitecore.WFFM.Core.Resources;
using IDs = Sitecore.WFFM.Analytics.Core.IDs;

namespace Sitecore.Support.Forms.Mvc.Processors
{
  public class SitecoreFormProcessor : FormProcessor
  {
    public SitecoreFormProcessor()
    {
      Init += FormInit;
      Submit += FormSubmit;
      Validate += FormValidate;
      SuccessValidation += FormSuccessValidation;
      FailedSubmit += FormFailedSubmit;
    }

    protected virtual string ClientMessage =>
      ResourceManager.Localize("FAILED_SUBMIT");

    private void FormatErrorMessage(FormModel model)
    {
      Assert.IsNotNull(model, "model");
      foreach (var failure in model.Failures)
      {
        var failure2 = failure;
        object[] parameters = {failure2.ErrorMessage, failure2.FailedAction};
        Log.Warn(
          "Web Forms for Marketers: an exception '{0}' has occured while trying to execute an action '{1}'.".FormatWith(
            parameters), this);
        if (!failure2.IsCustom && Settings.HideInnerError)
        {
          var contextDatabase = StaticSettings.ContextDatabase;
          if (contextDatabase != null)
          {
            var item = contextDatabase.GetItem(model.ID);
            if (item != null)
            {
              var str = item[FormIDs.SaveActionFailedMessage];
              if (!string.IsNullOrEmpty(str))
              {
                failure2.ErrorMessage = str;
                return;
              }
            }
            var item2 = contextDatabase.GetItem(FormIDs.SubmitErrorId);
            if (item2 != null)
            {
              var str2 = item2["Value"];
              if (!string.IsNullOrEmpty(str2))
              {
                failure2.ErrorMessage = str2;
                return;
              }
            }
          }
          failure2.ErrorMessage = ClientMessage;
        }
      }
      var source = from f in model.Failures
        group f by f.ErrorMessage
        into g
        select g.First();
      model.Failures = source.ToList();
    }

    public void FormFailedSubmit(object source, FormEventArgs args)
    {
      FormatErrorMessage(args.Form);
    }

    public void FormInit(object source, FormEventArgs args)
    {
      args.Form.Visible = true;
      args.Form.Submitted = false;
      args.Form.SuccessSubmit = false;
      args.Form.SaveSession = false;
      args.Form.Failures.Clear();
      args.Form.PageId = RenderingContext.CurrentOrNull.ContextItem.ID;
    }

    public void FormSubmit(object source, FormEventArgs args)
    {
      UpdateSubmitAnalytics(args.Form);
      UpdateSubmitCounter(args.Form);
    }

    public void FormSuccessValidation(object source, FormEventArgs args)
    {
      SaveAnalytics(args.Form);
    }

    public void FormValidate(object source, FormEventArgs args)
    {
      TrackValdationEvents(args);
    }

    protected void SaveAnalytics(FormModel model)
    {
      Assert.ArgumentNotNull(model, "model");
      try
      {
        FormDataHandler.ProcessData(model.ID,
          (from result in (from x in model.Sections select x.Fields).OfType<IFieldResult>() select result.GetResult())
            .ToArray(), model.Actions.ToArray());
      }
      catch (FormSubmitException exception)
      {
        model.Failures.AddRange(exception.Failures);
      }
      catch (Exception exception2)
      {
        try
        {
          var item = new ExecuteResult.Failure
          {
            ErrorMessage = exception2.Message,
            StackTrace = exception2.StackTrace
          };
          model.Failures.Add(item);
        }
        catch (Exception exception3)
        {
          Log.Error(exception3.Message, exception3, this);
        }
      }
      model.EventCounter = AnalyticsTracker.EventCounter + 1;
    }

    private void TrackValdationEvents(FormEventArgs args)
    {
      Assert.ArgumentNotNull(args, "args");
      if (args.Form.IsDropoutTrackingEnabled)
        foreach (var event2 in TrackingValidationErrorsProvider.Current.GetServerValidationEvents())
          AnalyticsTracker.TriggerEvent(event2);
    }

    private void UpdateSubmitAnalytics(FormModel model)
    {
      Assert.ArgumentNotNull(model, "model");
      if (model.IsAnalyticsEnabled)
      {
        AnalyticsTracker.BasePageTime = model.RenderedTime;
        AnalyticsTracker.TriggerEvent(IDs.FormSubmitEventId, "Form Submit", model.ID, string.Empty, model.ID.ToString());
      }
    }

    private void UpdateSubmitCounter(FormModel model)
    {
      Assert.ArgumentNotNull(model, "model");
      var source = (from x in model.Sections select x.Fields).OfType<CaptchaField>().ToArray();
      var field = source.FirstOrDefault(cf => (cf.RobotDetection != null) && cf.RobotDetection.Session.Enabled);
      var field2 = source.FirstOrDefault(cf => (cf.RobotDetection != null) && cf.RobotDetection.Server.Enabled);
      if (field != null)
        SubmitCounter.Session.AddSubmit(model.ID, field.RobotDetection.Session.MinutesInterval);
      if (field2 != null)
        SubmitCounter.Server.AddSubmit(model.ID, field2.RobotDetection.Server.MinutesInterval);
    }
  }
}