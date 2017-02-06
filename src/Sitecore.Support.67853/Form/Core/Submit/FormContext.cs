using System.Collections.Generic;
using Sitecore.Form.Core.Submit;

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