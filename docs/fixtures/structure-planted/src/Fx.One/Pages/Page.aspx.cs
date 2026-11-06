using System;
using System.Web.UI;
using Fx.Logic;
namespace Fx.One
{
    public class PageScreen : Page
    {
        private readonly GammaWorker worker = new GammaWorker();
        protected void Start(object sender, EventArgs e)
        {
            worker.Run(1);
        }
    }
}
