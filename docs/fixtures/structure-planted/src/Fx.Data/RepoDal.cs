using System.Data.SqlClient;
namespace Fx.Data
{
    public class RepoDal
    {
        public void Save(int id)
        {
            var command = new SqlCommand("INSERT INTO dbo.Orders (Id) VALUES (@id)");
            command.ExecuteNonQuery();
        }

        public void Audit(int id)
        {
            var command = new SqlCommand("EXEC dbo.usp_Audit");
            command.ExecuteNonQuery();
        }

        public void Missing(int id)
        {
            var command = new SqlCommand("EXEC dbo.usp_Missing");
            command.ExecuteNonQuery();
        }
    }
}
