using System;
using System.Data.SqlClient;

namespace Shop.Logic
{
    public interface IPayment { void Pay(string amount); }

    public class CardPayment : IPayment { public void Pay(string amount) { } }

    public class InvoicePayment : IPayment { public void Pay(string amount) { } }

    public class BasketService
    {
        private readonly IPayment payment;
        private readonly BasketStore store = new BasketStore();

        public void Checkout(string quantity)
        {
            store.Save(quantity);
            payment.Pay(quantity);
            var plugin = Activator.CreateInstance(Type.GetType("Shop.Plugins.Audit"));
            Walk(3);
        }

        public void Clear()
        {
            store.Remove();
        }

        private void Walk(int depth)
        {
            if (depth > 0) { Walk(depth - 1); }
        }
    }

    public class BasketStore
    {
        public void Save(string quantity)
        {
            var command = new SqlCommand("INSERT INTO Baskets (Quantity) VALUES (@q)");
            command.ExecuteNonQuery();
        }

        public void Remove()
        {
            var command = new SqlCommand("DELETE FROM Baskets");
            command.ExecuteNonQuery();
        }
    }
}
