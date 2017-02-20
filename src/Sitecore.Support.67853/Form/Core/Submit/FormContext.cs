using System.Collections.Generic;
using Sitecore.WFFM.Abstractions.Actions;

namespace Sitecore.Support.Form.Core.Submit
{
  internal class FormContext
  {
    public static List<ExecuteResult.Failure> Failures
    {
      get { return (List<ExecuteResult.Failure>) Context.Items["scwfm_failures"]; }
      set { Context.Items["scwfm_failures"] = value; }
    }
  }
}