
using MySql.Data.MySqlClient;

namespace BEACOMMER
{
    public class Database
    {
        private static readonly string connectionString =
            "Server=localhost;" +
            "Port=3306;" +
            "Database=beacommer_db;" +
            "Uid=root;" +
            "Pwd=;";

        public static MySqlConnection GetConnection()
        {
            return new MySqlConnection(connectionString);
        }
    }
}
