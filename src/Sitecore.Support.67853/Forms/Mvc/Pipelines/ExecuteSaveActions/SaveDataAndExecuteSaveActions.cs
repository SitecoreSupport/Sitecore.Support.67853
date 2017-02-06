using Sitecore.Diagnostics;
using Sitecore.Form.Core.ContentEditor.Data;
using Sitecore.Form.Core.Data;
using Sitecore.Form.Core.Submit;
using Sitecore.Forms.Mvc.Interfaces;
using Sitecore.Forms.Mvc.Models;
using Sitecore.Forms.Mvc.Pipelines;
using Sitecore.WFFM.Analytics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sitecore.Support.Forms.Mvc.Pipelines.ExecuteSaveActions
{
    public class SaveDataAndExecuteSaveActions : FormProcessorBase<IFormModel>
    {
        private ActionDefinition[] GetActions(ListDefinition definition)
        {
            Assert.ArgumentNotNull(definition, "definition");
            var list = new List<ActionDefinition>();
            if (!definition.Groups.Any()) return list.ToArray();

            foreach (var definition2 in definition.Groups)
                if (definition2.ListItems != null)
                    list.AddRange(from li in definition2.ListItems
                                  select new ActionDefinition(li.ItemID, li.Parameters) { UniqueKey = li.Unicid });
            return list.ToArray();
        }

        public override void Process(FormProcessorArgs<IFormModel> args)
        {
            Assert.ArgumentNotNull(args, "args");
            if (args.Model == null) return;
            var model = args.Model;

            try
            {
                Sitecore.Support.Form.Core.FormDataHandler.ProcessData(((FormModel)model).Item.ID, model.Results.ToArray(),
                  GetActions(model.Item.ActionsDefinition));
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
    }
}