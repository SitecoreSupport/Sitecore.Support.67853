using Sitecore.Data;
using Sitecore.Data.Items;
using Sitecore.Diagnostics;
using Sitecore.Form.Core;
using Sitecore.Form.Core.Configuration;
using Sitecore.Form.Core.Controls.Data;
using Sitecore.Form.Core.Data;
using Sitecore.Form.Core.Submit;
using Sitecore.Forms.Core.Data;
using Sitecore.Forms.Mvc.Data.Analytics;
using Sitecore.Forms.Mvc.Events;
using Sitecore.Forms.Mvc.Interfaces;
using Sitecore.Forms.Mvc.Models;
using Sitecore.Forms.Mvc.Models.Fields;
using Sitecore.Mvc.Presentation;
using Sitecore.StringExtensions;
using Sitecore.WFFM.Analytics;
using Sitecore.WFFM.Analytics.Core;
using Sitecore.WFFM.Analytics.Events;
using Sitecore.WFFM.Core.Resources;
using System;
using System.Collections.Generic;
using System.Linq;
using Sitecore.Forms.Mvc.Processors;

namespace Sitecore.Support.Forms.Mvc.Processors
{
  public class SitecoreFormProcessor : FormProcessor
  {
    protected virtual string ClientMessage
    {
      get
      {
        return ResourceManager.Localize("FAILED_SUBMIT");
      }
    }

    public SitecoreFormProcessor()
    {
      base.Init += new FormEventRaised(this.FormInit);
      base.Submit += new FormEventRaised(this.FormSubmit);
      base.Validate += new FormEventRaised(this.FormValidate);
      base.SuccessValidation += new FormEventRaised(this.FormSuccessValidation);
      base.FailedSubmit += new FormEventRaised(this.FormFailedSubmit);
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

    public void FormFailedSubmit(object source, FormEventArgs args)
    {
      this.FormatErrorMessage(args.Form);
    }

    public void FormSuccessValidation(object source, FormEventArgs args)
    {
      this.SaveAnalytics(args.Form);
    }

    public void FormSubmit(object source, FormEventArgs args)
    {
      this.UpdateSubmitAnalytics(args.Form);
      this.UpdateSubmitCounter(args.Form);
    }

    public void FormValidate(object source, FormEventArgs args)
    {
      this.TrackValdationEvents(args);
    }

    protected void SaveAnalytics(FormModel model)
    {
      Assert.ArgumentNotNull(model, "model");
      try
      {
        Sitecore.Support.Form.Core.FormDataHandler.ProcessData(model.ID, (from result in model.Sections.SelectMany((SectionModel x) => x.Fields).OfType<IFieldResult>()
                                               select result.GetResult()).ToArray<ControlResult>(), model.Actions.ToArray());
      }
      catch (FormSubmitException ex)
      {
        model.Failures.AddRange(ex.Failures);
      }
      catch (Exception ex2)
      {
        try
        {
          model.Failures.Add(new ExecuteResult.Failure
          {
            ErrorMessage = ex2.Message,
            StackTrace = ex2.StackTrace
          });
        }
        catch (Exception ex3)
        {
          Log.Error(ex3.Message, ex3, this);
        }
      }
      model.EventCounter = AnalyticsTracker.EventCounter + 1;
    }

    private void TrackValdationEvents(FormEventArgs args)
    {
      Assert.ArgumentNotNull(args, "args");
      if (args.Form.IsDropoutTrackingEnabled)
      {
        foreach (ServerEvent current in TrackingValidationErrorsProvider.Current.GetServerValidationEvents())
        {
          AnalyticsTracker.TriggerEvent(current);
        }
      }
    }

    private void UpdateSubmitAnalytics(FormModel model)
    {
      Assert.ArgumentNotNull(model, "model");
      if (model.IsAnalyticsEnabled)
      {
        AnalyticsTracker.BasePageTime = model.RenderedTime;
        AnalyticsTracker.TriggerEvent(Sitecore.WFFM.Analytics.Core.IDs.FormSubmitEventId, "Form Submit", model.ID, string.Empty, model.ID.ToString());
      }
    }

    private void UpdateSubmitCounter(FormModel model)
    {
      Assert.ArgumentNotNull(model, "model");
      CaptchaField[] source = model.Sections.SelectMany((SectionModel x) => x.Fields).OfType<CaptchaField>().ToArray<CaptchaField>();
      CaptchaField captchaField = source.FirstOrDefault((CaptchaField cf) => cf.RobotDetection != null && cf.RobotDetection.Session.Enabled);
      CaptchaField captchaField2 = source.FirstOrDefault((CaptchaField cf) => cf.RobotDetection != null && cf.RobotDetection.Server.Enabled);
      if (captchaField != null)
      {
        SubmitCounter.Session.AddSubmit(model.ID, captchaField.RobotDetection.Session.MinutesInterval);
      }
      if (captchaField2 != null)
      {
        SubmitCounter.Server.AddSubmit(model.ID, captchaField2.RobotDetection.Server.MinutesInterval);
      }
    }

    private void FormatErrorMessage(FormModel model)
    {
      Assert.IsNotNull(model, "model");
      foreach (ExecuteResult.Failure current in model.Failures)
      {
        ExecuteResult.Failure failure = current;
        Log.Warn("Web Forms for Marketers: an exception '{0}' has occured while trying to execute an action '{1}'.".FormatWith(new object[]
        {
                    failure.ErrorMessage,
                    failure.FailedAction
        }), this);
        if (!failure.IsCustom && Settings.HideInnerError)
        {
          Database contextDatabase = StaticSettings.ContextDatabase;
          if (contextDatabase != null)
          {
            Item item = contextDatabase.GetItem(model.ID);
            if (item != null)
            {
              string text = item[FormIDs.SaveActionFailedMessage];
              if (!string.IsNullOrEmpty(text))
              {
                failure.ErrorMessage = text;
                return;
              }
            }
            Item item2 = contextDatabase.GetItem(FormIDs.SubmitErrorId);
            if (item2 != null)
            {
              string text2 = item2["Value"];
              if (!string.IsNullOrEmpty(text2))
              {
                failure.ErrorMessage = text2;
                return;
              }
            }
          }
          failure.ErrorMessage = this.ClientMessage;
        }
      }
      IEnumerable<ExecuteResult.Failure> source = from f in model.Failures
                                                  group f by f.ErrorMessage into g
                                                  select g.First<ExecuteResult.Failure>();
      model.Failures = source.ToList<ExecuteResult.Failure>();
    }
  }
}