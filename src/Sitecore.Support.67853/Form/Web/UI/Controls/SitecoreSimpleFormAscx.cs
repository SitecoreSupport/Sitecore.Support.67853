using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Web.UI;
using System.Web.UI.WebControls;
using Sitecore.Diagnostics;
using Sitecore.Form.Core.Client.Submit;
using Sitecore.Form.Core.Configuration;
using Sitecore.Form.Core.Data;
using Sitecore.Form.Core.Pipelines.FormSubmit;
using Sitecore.Form.Core.Submit;
using Sitecore.Form.Web.UI.Controls;
using Sitecore.Forms.Core.Data;
using Sitecore.Pipelines;
using Sitecore.Support.Form.Core;
using Sitecore.WFFM.Analytics;
using IDs = Sitecore.WFFM.Analytics.Core.IDs;

namespace Sitecore.Support.Form.Web.UI.Controls
{
  public class SitecoreSimpleFormAscx : Sitecore.Form.Web.UI.Controls.SitecoreSimpleFormAscx
  {
    // Methods
    protected override void OnClick(object sender, EventArgs e)
    {
      Assert.ArgumentNotNull(sender, "sender");
      UpdateSubmitAnalytics();
      UpdateSubmitCounter();
      var flag = false;
      var collection = (Page ?? new Page()).GetValidators(((Control) sender).ID);
      if (collection.FirstOrDefault(v => !v.IsValid && v is IAttackProtection) != null)
        collection.ForEach(delegate(IValidator v)
        {
          if (!(v.IsValid || v is IAttackProtection))
            v.IsValid = true;
        });
      if ((Page != null) && Page.IsValid)
      {
        RequiredMarkerProccess(this, true);
        var list = new List<ActionDefinition>();
        CollectActions(this, list);
        try
        {
          FormDataHandler.ProcessData(FormID, GetChildState().ToArray(), list.ToArray());
          OnSuccessSubmit();
          OnSucceedValidation(new EventArgs());
          OnSucceedSubmit(new EventArgs());
        }
        catch (ThreadAbortException)
        {
          flag = true;
        }
        catch (ValidatorException exception2)
        {
          string[] messages = {exception2.Message};
          OnRefreshError(messages);
        }
        catch (FormSubmitException exception3)
        {
          flag = true;
          OnRefreshError((from f in exception3.Failures select f.ErrorMessage).ToArray());
        }
        catch (Exception exception4)
        {
          try
          {
            var failureArray1 = new ExecuteResult.Failure[1];
            var failure = new ExecuteResult.Failure();
            failure.ErrorMessage = exception4.ToString();
            failure.StackTrace = exception4.StackTrace;
            failureArray1[0] = failure;
            var args2 = new SubmittedFormFailuresArgs(FormID, failureArray1)
            {
              Database = StaticSettings.ContextDatabase.Name
            };
            CorePipeline.Run("errorSubmit", args2);
            OnRefreshError((from f in args2.Failures select f.ErrorMessage).ToArray());
          }
          catch (Exception exception5)
          {
            Log.Error(exception5.Message, exception5, this);
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
      EventCounter.Value = (AnalyticsTracker.EventCounter + 1).ToString();
      if (flag)
        OnSucceedValidation(new EventArgs());
      OnFailedSubmit(new EventArgs());
    }

    private void OnFailedSubmit(EventArgs e)
    {
      FailedSubmit?.Invoke(this, e);
    }

    private void OnSucceedSubmit(EventArgs e)
    {
      var succeedSubmit = SucceedSubmit;
      if (succeedSubmit != null)
        succeedSubmit(this, e);
    }

    private void OnSucceedValidation(EventArgs args)
    {
      var succeedValidation = SucceedValidation;
      if (succeedValidation != null)
        succeedValidation(this, args);
    }

    private void SetFocusOnError()
    {
      if (Page != null)
      {
        var validator = (BaseValidator)
          Page.Validators.FirstOrDefault(v => v is BaseValidator && ((BaseValidator) v).IsFailedAndRequireFocus());
        if (validator != null)
          if (!string.IsNullOrEmpty(validator.Text))
          {
            var controlToValidate = validator.GetControlToValidate();
            if (controlToValidate != null)
              SetFocus(validator.ClientID, controlToValidate.ClientID);
          }
          else
          {
            var control2 = FindControl(BaseID + PrefixErrorID);
            if (control2 != null)
              SetFocus(control2.ClientID, null);
          }
      }
    }

    private void TrackValdationEvents(object sender, EventArgs e)
    {
      if (IsDropoutTrackingEnabled)
        OnTrackValidationEvent(sender, e);
    }

    private void UpdateSubmitAnalytics()
    {
      if (!(!IsAnalyticsEnabled || FastPreview))
      {
        AnalyticsTracker.BasePageTime = RenderedTime;
        AnalyticsTracker.TriggerEvent(IDs.FormSubmitEventId, "Form Submit", FormID, string.Empty,
          FormID.ToString());
      }
    }

    private void UpdateSubmitCounter()
    {
      if (RobotDetection.Session.Enabled)
        SubmitCounter.Session.AddSubmit(FormID, RobotDetection.Session.MinutesInterval);
      if (RobotDetection.Server.Enabled)
        SubmitCounter.Server.AddSubmit(FormID, RobotDetection.Server.MinutesInterval);
    }

    public new event EventHandler<EventArgs> SucceedSubmit;

    public new event EventHandler<EventArgs> SucceedValidation;

    public new event EventHandler<EventArgs> FailedSubmit;
  }
}