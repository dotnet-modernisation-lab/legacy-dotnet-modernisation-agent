using System;
using System.Web.UI;
using Fx.Data;
namespace Fx.Two
{
    public class PageScreen : Page
    {
        private readonly RepoDal worker = new RepoDal();
        protected void Start(object sender, EventArgs e)
        {
            worker.Save(2); worker.Audit(2); worker.Missing(2);
        }
    }
}
