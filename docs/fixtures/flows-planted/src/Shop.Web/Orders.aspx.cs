using System;
using System.Web.UI;
using Shop.Logic;

namespace Shop.Web
{
    public class OrderScreen : Page
    {
        private readonly BasketService basket = new BasketService();

        protected void PlaceOrder_Click(object sender, EventArgs e)
        {
            var qty = Request.Form["quantity"];
            Session["LastOrder"] = qty;
            basket.Checkout(qty);
        }

        protected void CancelOrder(object sender, EventArgs e)
        {
            basket.Clear();
        }

        protected void Orphan_Click(object sender, EventArgs e)
        {
        }
    }
}
