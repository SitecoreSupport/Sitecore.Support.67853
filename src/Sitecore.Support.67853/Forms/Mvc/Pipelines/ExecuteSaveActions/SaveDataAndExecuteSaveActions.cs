using System;
using System.Collections.Generic;
using System.Linq;
using Sitecore.Diagnostics;
using Sitecore.Form.Core.Data;
using Sitecore.Forms.Mvc.Interfaces;
using Sitecore.Forms.Mvc.Pipelines;
using Sitecore.WFFM.Abstractions.Actions;
using Sitecore.WFFM.Abstractions.ContentEditor;
using Sitecore.WFFM.Abstractions.Shared;
using FormDataHandler = Sitecore.Support.Forms.Core.Handlers.FormDataHandler;

namespace Sitecore.Support.Forms.Mvc.Pipelines.ExecuteSaveActions
{
  public class SaveDataAndExecuteSaveActions : FormProcessorBase<IFormModel>
  {
    private readonly IAnalyticsTracker analyticsTracker;
    private readonly FormDataHandler formDataHandler;

    public SaveDataAndExecuteSaveActions(FormDataHandler formDataHandler, IAnalyticsTracker analyticsTracker)
    {
      Assert.ArgumentNotNull(formDataHandler, "formDataHandler");
      Assert.ArgumentNotNull(analyticsTracker, "analyticsTracker");
      this.formDataHandler = formDataHandler;
      this.analyticsTracker = analyticsTracker;
    }

    private IActionDefinition[] GetActions(IListDefinition definition)
    {
      Assert.ArgumentNotNull(definition, "definition");
      var list = new List<IActionDefinition>();

      if (!definition.Groups.Any()) return list.ToArray();

      foreach (var definition2 in definition.Groups)
      {
        if (definition2.ListItems != null)
        {
          list.AddRange(from li in definition2.ListItems select new ActionDefinition(li.ItemID, li.Parameters) { UniqueKey = li.Unicid });
        }
      }

      return list.ToArray();
    }

    public override void Process(FormProcessorArgs<IFormModel> args)
    {
      Assert.ArgumentNotNull(args, "args");
      if (args.Model == null) return;

      var model = args.Model;

      try
      {
        formDataHandler.ProcessForm(((Sitecore.Forms.Mvc.Models.FormModel)model).Item.ID, model.Results.ToArray(), GetActions(model.Item.ActionsDefinition));
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
            StackTrace = exception2.StackTrace,
            IsMessageUnchangeable = exception2.Source.Equals(WFFM.Abstractions.Constants.Core.Constants.CheckActions)
          };

          model.Failures.Add(item);
        }
        catch (Exception exception3)
        {
          Log.Error(exception3.Message, exception3, this);
        }
      }
      model.EventCounter = analyticsTracker.EventCounter + 1;
    }
  }
}
