using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Web.UI;
using Sitecore.Diagnostics;
using Sitecore.Form.Core.Client.Submit;
using Sitecore.Form.Core.Configuration;
using Sitecore.Form.Core.Data;
using Sitecore.Form.Core.Pipelines.FormSubmit;
using Sitecore.Form.Core.Utility;
using Sitecore.Form.Web.UI.Controls;
using Sitecore.Forms.Core.Data;
using Sitecore.Pipelines;
using Sitecore.Support.Forms.Core.Handlers;
using Sitecore.WFFM.Abstractions.Actions;
using Sitecore.WFFM.Abstractions.Dependencies;
using BaseValidator = System.Web.UI.WebControls.BaseValidator;

namespace Sitecore.Support.Form.Web.UI.Controls
{
  public class SitecoreSimpleFormAscx : Sitecore.Form.Web.UI.Controls.SitecoreSimpleFormAscx
  {
    protected override void OnClick(object sender, EventArgs e)
    {
      Assert.ArgumentNotNull(sender, "sender");
      var sessionValue = SessionUtil.GetSessionValue<object>(AntiCsrf.ID);

      if ((sessionValue == null) || (sessionValue.ToString() != AntiCsrf.Value))
      {
        var failures = new ExecuteResult.Failure[1];
        var failure = new ExecuteResult.Failure
        {
          ErrorMessage = "WFFM: Forged request detected!"
        };

        failures[0] = failure;

        var args = new SubmittedFormFailuresArgs(FormID, failures)
        {
          Database = StaticSettings.ContextDatabase.Name
        };

        CorePipeline.Run("errorSubmit", args);

        Log.Error("WFFM: Forged request detected!", this);
        OnRefreshError((from f in args.Failures select f.ErrorMessage).ToArray());
      }
      else
      {
        UpdateSubmitAnalytics();
        UpdateSubmitCounter();

        var flag = false;
        var collection = (Page ?? new Page()).GetValidators(((Control)sender).ID);

        if (collection.FirstOrDefault(v => (!v.IsValid && (v is IAttackProtection))) != null)
        {
          collection.ForEach(delegate (IValidator v)
          {
            if (!v.IsValid && !(v is IAttackProtection))
            {
              v.IsValid = true;
            }
          });
        }

        if ((Page != null) && Page.IsValid)
        {
          RequiredMarkerProccess(this, true);
          var list = new List<IActionDefinition>();
          CollectActions(this, list);

          try
          {
            DependenciesManager.Resolve<FormDataHandler>().ProcessForm(FormID, GetChildState().ToArray(), list.ToArray());

            OnSuccessSubmit();
            OnSucceedValidation(new EventArgs());
            OnSucceedSubmit(new EventArgs());
          }
          catch (ThreadAbortException)
          {
            flag = true;
          }
          catch (ValidatorException exception)
          {
            OnRefreshError(new[] { exception.Message });
          }
          catch (FormSubmitException exception2)
          {
            flag = true;
            OnRefreshError((from f in exception2.Failures select f.ErrorMessage).ToArray());
          }
          catch (Exception exception3)
          {
            try
            {
              var failureArray2 = new ExecuteResult.Failure[1];

              var failure2 = new ExecuteResult.Failure
              {
                ErrorMessage = exception3.ToString(),
                StackTrace = exception3.StackTrace
              };

              failureArray2[0] = failure2;
              var args3 = new SubmittedFormFailuresArgs(FormID, failureArray2)
              {
                Database = StaticSettings.ContextDatabase.Name
              };

              CorePipeline.Run("errorSubmit", args3);
              OnRefreshError((from f in args3.Failures select f.ErrorMessage).ToArray());
            }
            catch (Exception exception4)
            {
              Log.Error(exception4.Message, exception4, this);
            }
            flag = true;
          }
        }
        else
        {
          SetFocusOnError();
          TrackValdationEvents(sender, e);
          RequiredMarkerProccess(this, false);
        }
        EventCounter.Value = (DependenciesManager.AnalyticsTracker.EventCounter + 1).ToString();

        if (flag)
        {
          OnSucceedValidation(new EventArgs());
        }

        OnFailedSubmit(new EventArgs());
      }
    }

    private void OnFailedSubmit(EventArgs e)
    {
      var failedSubmit = FailedSubmit;
      failedSubmit?.Invoke(this, e);
    }

    private void UpdateSubmitAnalytics()
    {
      if (!IsAnalyticsEnabled || FastPreview) return;

      DependenciesManager.AnalyticsTracker.BasePageTime = RenderedTime;
      DependenciesManager.AnalyticsTracker.TriggerEvent(WFFM.Abstractions.Analytics.IDs.FormSubmitEventId, "Form Submit", FormID, string.Empty, FormID.ToString());
    }

    private void SetFocusOnError()
    {
      var validator = (BaseValidator) Page?.Validators.FirstOrDefault(v =>
      {
        var baseValidator = v as BaseValidator;
        return baseValidator != null && baseValidator.IsFailedAndRequireFocus();
      });

      if (validator == null) return;

      if (!string.IsNullOrEmpty(validator.Text))
      {
        var controlToValidate = validator.GetControlToValidate();

        if (controlToValidate == null) return;
        SetFocus(validator.ClientID, controlToValidate.ClientID);
      }
      else
      {
        var control = FindControl(BaseID + prefixErrorID);

        if (control == null) return;
        SetFocus(control.ClientID, null);
      }
    }

    private void UpdateSubmitCounter()
    {
      if (RobotDetection.Session.Enabled) SubmitCounter.Session.AddSubmit(FormID, RobotDetection.Session.MinutesInterval);
      if (!RobotDetection.Server.Enabled) return;

      SubmitCounter.Server.AddSubmit(FormID, RobotDetection.Server.MinutesInterval);
    }

    private void OnSucceedValidation(EventArgs args)
    {
      var succeedValidation = SucceedValidation;
      succeedValidation?.Invoke(this, args);
    }

    private void OnSucceedSubmit(EventArgs e)
    {
      var succeedSubmit = SucceedSubmit;
      succeedSubmit?.Invoke(this, e);
    }

    private void TrackValdationEvents(object sender, EventArgs e)
    {
      if (!IsDropoutTrackingEnabled) return;
      OnTrackValidationEvent(sender, e);
    }

    public new event EventHandler<EventArgs> SucceedValidation;
    public new event EventHandler<EventArgs> SucceedSubmit;
    public new event EventHandler<EventArgs> FailedSubmit;
  }
}
