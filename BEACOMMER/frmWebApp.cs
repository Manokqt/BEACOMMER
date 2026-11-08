using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using MySql.Data.MySqlClient;

namespace BEACOMMER
{
    public partial class frmWebApp : Form
    {
        private WebView2 webView;
        private string webFolder = "";
        private string loggedInRole = "";
        private int loggedInUserId = 0;

        private const int MaxImageSize = 5 * 1024 * 1024;

        private static readonly string[] OrderStatuses =
            { "Pending", "Processing", "Completed", "Cancelled" };

        private static readonly string[] PaymentMethods =
            { "Cash on Delivery", "GCash", "Credit/Debit Card", "Bank Transfer" };

        private class CartLine
        {
            public int ProductId;
            public string Name;
            public decimal Price;
            public int Quantity;
        }

        public frmWebApp()
        {
            InitializeComponent();

            webView = new WebView2();
            webView.Dock = DockStyle.Fill;

            this.Controls.Add(webView);

            this.Text = "BEACOMMER Beauty Shop";
            this.WindowState = FormWindowState.Maximized;
            this.StartPosition = FormStartPosition.CenterScreen;

            this.Load += frmWebApp_Load;
        }

        // =====================================
        // LOAD HTML WEBSITE
        // =====================================

        private async void frmWebApp_Load(object sender, EventArgs e)
        {
            try
            {
                await webView.EnsureCoreWebView2Async();

                webView.CoreWebView2.WebMessageReceived +=
                    WebView_WebMessageReceived;

                string htmlPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Web",
                    "index.html"
                );

                if (!File.Exists(htmlPath))
                {
                    string projectPath = Path.GetFullPath(
                        Path.Combine(
                            AppDomain.CurrentDomain.BaseDirectory,
                            "..", "..", "..", "Web", "index.html"
                        )
                    );

                    if (File.Exists(projectPath))
                        htmlPath = projectPath;
                }

                if (!File.Exists(htmlPath))
                {
                    MessageBox.Show(
                        "index.html was not found!\n\n" + htmlPath,
                        "BEACOMMER Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                    return;
                }

                // Save images beside the HTML folder.
                webFolder = Path.GetDirectoryName(htmlPath);

                webView.Source = new Uri(htmlPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "WebView2 Error:\n" + ex.Message,
                    "BEACOMMER",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        // =====================================
        // SEND RESPONSE TO JAVASCRIPT
        // =====================================

        private void SendResponse(object response)
        {
            try
            {
                if (webView?.CoreWebView2 == null)
                    return;

                string json = JsonSerializer.Serialize(response);
                webView.CoreWebView2.PostWebMessageAsJson(json);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Response Error: " + ex.Message, "BEACOMMER");
            }
        }

        private void Fail(string action, string message)
        {
            SendResponse(new { action = action, success = false, message = message });
        }

        private void Ok(string action, string message)
        {
            SendResponse(new { action = action, success = true, message = message });
        }

        // =====================================
        // JSON / VALUE HELPERS
        // =====================================

        private string GetString(JsonElement root, string property)
        {
            if (root.TryGetProperty(property, out JsonElement value))
            {
                if (value.ValueKind == JsonValueKind.String)
                    return value.GetString() ?? "";

                if (value.ValueKind != JsonValueKind.Null)
                    return value.ToString();
            }

            return "";
        }

        private int GetInt(JsonElement root, string property)
        {
            if (root.TryGetProperty(property, out JsonElement value))
            {
                if (value.ValueKind == JsonValueKind.Number &&
                    value.TryGetInt32(out int result))
                    return result;

                if (int.TryParse(value.ToString(), out result))
                    return result;
            }

            return 0;
        }

        private decimal GetDecimal(JsonElement root, string property)
        {
            if (root.TryGetProperty(property, out JsonElement value))
            {
                if (value.ValueKind == JsonValueKind.Number &&
                    value.TryGetDecimal(out decimal result))
                    return result;

                if (decimal.TryParse(
                    value.ToString(),
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out result))
                    return result;
            }

            return 0;
        }

        private static string Str(object v)
        {
            return v == null || v == DBNull.Value ? "" : v.ToString();
        }

        private static string Fmt(object v)
        {
            return v == null || v == DBNull.Value
                ? ""
                : Convert.ToDateTime(v).ToString("yyyy-MM-dd HH:mm");
        }

        private static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return true; // optional
            int at = email.IndexOf('@');
            return at > 0 && email.IndexOf('.', at) > at + 1 && !email.Contains(" ");
        }

        private bool IsBcryptHash(string password)
        {
            return password.StartsWith("$2a$") ||
                   password.StartsWith("$2b$") ||
                   password.StartsWith("$2y$");
        }

        private bool PasswordMatches(string plain, string stored)
        {
            if (IsBcryptHash(stored))
            {
                try { return BCrypt.Net.BCrypt.Verify(plain, stored); }
                catch { return false; }
            }

            return string.Equals(plain, stored);
        }

        private object Scalar(MySqlConnection conn, string sql, int uid = 0)
        {
            using (MySqlCommand cmd = new MySqlCommand(sql, conn))
            {
                if (sql.Contains("@uid"))
                    cmd.Parameters.AddWithValue("@uid", uid);

                return cmd.ExecuteScalar();
            }
        }

        // =====================================
        // ACCESS CHECKS
        // =====================================

        private bool RequireLogin(string action)
        {
            if (loggedInUserId <= 0)
            {
                Fail(action, "Please log in first.");
                return false;
            }
            return true;
        }

        private bool RequireAdmin(string action)
        {
            if (loggedInUserId <= 0 || loggedInRole != "admin")
            {
                Fail(action, "Administrator access is required.");
                return false;
            }
            return true;
        }

        private bool RequireCustomer(string action)
        {
            if (loggedInUserId <= 0 || loggedInRole != "customer")
            {
                Fail(action, "A customer account is required.");
                return false;
            }
            return true;
        }

        // =====================================
        // RECEIVE JAVASCRIPT MESSAGES
        // =====================================

        private void WebView_WebMessageReceived(
            object sender,
            CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string message = e.WebMessageAsJson;

                using (JsonDocument doc = JsonDocument.Parse(message))
                {
                    JsonElement root = doc.RootElement;

                    // Support both object and JSON-string messages.
                    if (root.ValueKind == JsonValueKind.String)
                    {
                        message = root.GetString() ?? "{}";

                        using (JsonDocument innerDoc = JsonDocument.Parse(message))
                        {
                            HandleAction(innerDoc.RootElement);
                        }
                    }
                    else
                    {
                        HandleAction(root);
                    }
                }
            }
            catch (Exception ex)
            {
                Fail("error", "Application Error: " + ex.Message);
            }
        }

        private void HandleAction(JsonElement root)
        {
            string action = GetString(root, "action");

            if (string.IsNullOrWhiteSpace(action))
            {
                Fail("error", "Missing action.");
                return;
            }

            switch (action)
            {
                // ---------- AUTH ----------
                case "login":
                    LoginUser(GetString(root, "username"), GetString(root, "password"));
                    break;

                case "register":
                    RegisterUser(
                        GetString(root, "username"),
                        GetString(root, "fullname"),
                        GetString(root, "email"),
                        GetString(root, "phone"),
                        GetString(root, "password"));
                    break;

                case "logout":
                    loggedInRole = "";
                    loggedInUserId = 0;
                    Ok("logout", "Successfully logged out.");
                    break;

                // ---------- SHARED (any logged-in user) ----------
                case "getCategories":
                    if (RequireLogin(action)) GetCategories();
                    break;

                case "getProducts":
                    if (RequireLogin(action)) GetProducts();
                    break;

                case "getProfile":
                    if (RequireLogin(action)) GetProfile();
                    break;

                case "updateProfile":
                    if (RequireLogin(action)) UpdateProfile(root);
                    break;

                case "changePassword":
                    if (RequireLogin(action))
                        ChangePassword(GetString(root, "currentPassword"),
                                       GetString(root, "newPassword"));
                    break;

                case "getOrderDetails":
                    if (RequireLogin(action)) GetOrderDetails(GetInt(root, "id"));
                    break;

                // ---------- ADMIN ----------
                case "addCategory":
                    if (RequireAdmin(action)) AddCategory(GetString(root, "category_name"));
                    break;

                case "deleteCategory":
                    if (RequireAdmin(action)) DeleteCategory(GetInt(root, "id"));
                    break;

                case "addProduct":
                    if (RequireAdmin(action)) AddProduct(root);
                    break;

                case "deleteProduct":
                    if (RequireAdmin(action)) DeleteProduct(GetInt(root, "id"));
                    break;

                case "getDashboard":
                    if (RequireAdmin(action)) GetAdminDashboard();
                    break;

                case "getCustomers":
                    if (RequireAdmin(action)) GetCustomers();
                    break;

                case "getOrders":
                    if (RequireAdmin(action)) GetOrders();
                    break;

                case "getPayments":
                    if (RequireAdmin(action)) GetPayments();
                    break;

                case "getReports":
                    if (RequireAdmin(action)) GetReports();
                    break;

                case "updateOrderStatus":
                    if (RequireAdmin(action))
                        UpdateOrderStatus(GetInt(root, "id"), GetString(root, "status"));
                    break;

                // ---------- CUSTOMER ----------
                case "getCustomerDashboard":
                    if (RequireCustomer(action)) GetCustomerDashboard();
                    break;

                case "getMyOrders":
                    if (RequireCustomer(action)) GetMyOrders();
                    break;

                case "cancelOrder":
                    if (RequireCustomer(action)) CancelOrder(GetInt(root, "id"));
                    break;

                case "checkout":
                    if (RequireCustomer(action)) Checkout(root);
                    break;

                default:
                    Fail(action, "Action is not yet implemented: " + action);
                    break;
            }
        }

        // =====================================
        // CATEGORIES
        // =====================================

        private void GetCategories()
        {
            try
            {
                using (MySqlConnection conn = Database.GetConnection())
                {
                    conn.Open();

                    using (MySqlCommand cmd = new MySqlCommand(
                        "SELECT id, category_name FROM categories ORDER BY category_name", conn))
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        List<object> list = new List<object>();

                        while (reader.Read())
                        {
                            list.Add(new
                            {
                                id = Convert.ToInt32(reader["id"]),
                                category_name = Str(reader["category_name"])
                            });
                        }

                        SendResponse(new { action = "getCategories", success = true, data = list });
                    }
                }
            }
            catch (Exception ex) { Fail("getCategories", ex.Message); }
        }

        private void AddCategory(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName))
            {
                Fail("addCategory", "Please enter a category name.");
                return;
            }

            try
            {
                using (MySqlConnection conn = Database.GetConnection())
                {
                    conn.Open();

                    using (MySqlCommand cmd = new MySqlCommand(
                        "INSERT INTO categories (category_name) VALUES (@n)", conn))
                    {
                        cmd.Parameters.AddWithValue("@n", categoryName.Trim());
                        cmd.ExecuteNonQuery();
                    }
                }

                Ok("addCategory", "Category added successfully!");
            }
            catch (MySqlException ex)
            {
                Fail("addCategory", ex.Number == 1062 ? "This category already exists." : ex.Message);
            }
            catch (Exception ex) { Fail("addCategory", ex.Message); }
        }

        private void DeleteCategory(int id)
        {
            if (id <= 0) { Fail("deleteCategory", "Invalid category ID."); return; }

            try
            {
                using (MySqlConnection conn = Database.GetConnection())
                {
                    conn.Open();

                    using (MySqlCommand cmd = new MySqlCommand(
                        "DELETE FROM categories WHERE id = @id", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        int result = cmd.ExecuteNonQuery();

                        if (result > 0) Ok("deleteCategory", "Category deleted successfully!");
                        else Fail("deleteCategory", "Category was not found.");
                    }
                }
            }
            catch (MySqlException ex)
            {
                Fail("deleteCategory",
                    "Cannot delete this category. It may still be used by products. " + ex.Message);
            }
            catch (Exception ex) { Fail("deleteCategory", ex.Message); }
        }

        // =====================================
        // PRODUCTS
        // =====================================

        private void GetProducts()
        {
            try
            {
                using (MySqlConnection conn = Database.GetConnection())
                {
                    conn.Open();

                    string query = @"
                        SELECT p.id, p.product_name, p.category_id, c.category_name,
                               p.description, p.price, p.stock, p.image_path
                        FROM products p
                        LEFT JOIN categories c ON p.category_id = c.id
                        ORDER BY p.id DESC";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        List<object> list = new List<object>();

                        while (reader.Read())
                        {
                            list.Add(new
                            {
                                id = Convert.ToInt32(reader["id"]),
                                product_name = Str(reader["product_name"]),
                                category_id = reader["category_id"] == DBNull.Value
                                    ? 0 : Convert.ToInt32(reader["category_id"]),
                                category_name = reader["category_name"] == DBNull.Value
                                    ? "Uncategorized" : Str(reader["category_name"]),
                                description = Str(reader["description"]),
                                price = Convert.ToDecimal(reader["price"]),
                                stock = Convert.ToInt32(reader["stock"]),
                                image_path = Str(reader["image_path"])
                            });
                        }

                        SendResponse(new { action = "getProducts", success = true, data = list });
                    }
                }
            }
            catch (Exception ex) { Fail("getProducts", ex.Message); }
        }

        private void AddProduct(JsonElement root)
        {
            string productName = GetString(root, "product_name");
            int categoryId = GetInt(root, "category_id");
            string description = GetString(root, "description");
            decimal price = GetDecimal(root, "price");
            int stock = GetInt(root, "stock");
            string imageBase64 = GetString(root, "imageBase64");

            if (string.IsNullOrWhiteSpace(productName))
            {
                Fail("addProduct", "Please enter a product name.");
                return;
            }

            if (categoryId <= 0 || price < 0 || stock < 0)
            {
                Fail("addProduct", "Please check the category, price, and stock.");
                return;
            }

            string savedImagePath = "";

            try
            {
                using (MySqlConnection conn = Database.GetConnection())
                {
                    conn.Open();

                    if (Convert.ToInt32(Scalar(conn,
                        "SELECT COUNT(*) FROM categories WHERE id = " + categoryId)) == 0)
                    {
                        Fail("addProduct", "Selected category does not exist.");
                        return;
                    }

                    if (!string.IsNullOrWhiteSpace(imageBase64))
                        savedImagePath = SaveProductImage(imageBase64);

                    string query = @"
                        INSERT INTO products
                        (product_name, category_id, description, price, stock, image_path)
                        VALUES
                        (@product_name, @category_id, @description, @price, @stock, @image_path)";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@product_name", productName.Trim());
                        cmd.Parameters.AddWithValue("@category_id", categoryId);
                        cmd.Parameters.AddWithValue("@description", description);
                        cmd.Parameters.AddWithValue("@price", price);
                        cmd.Parameters.AddWithValue("@stock", stock);
                        cmd.Parameters.AddWithValue("@image_path",
                            string.IsNullOrEmpty(savedImagePath)
                                ? (object)DBNull.Value : savedImagePath);

                        if (cmd.ExecuteNonQuery() <= 0)
                            throw new Exception("Product was not inserted.");
                    }
                }

                SendResponse(new
                {
                    action = "addProduct",
                    success = true,
                    message = "Product added successfully!",
                    image_path = savedImagePath
                });
            }
            catch (Exception ex)
            {
                DeleteImageFile(savedImagePath); // don't leave orphan images
                Fail("addProduct", "Unable to save product: " + ex.Message);
            }
        }

        private string SaveProductImage(string base64Data)
        {
            try
            {
                string base64 = base64Data;
                string extension;

                if (!base64.Contains(","))
                    throw new Exception("Invalid image data.");

                string header = base64.Substring(0, base64.IndexOf(','));

                if (header.Contains("image/png")) extension = ".png";
                else if (header.Contains("image/jpeg")) extension = ".jpg";
                else if (header.Contains("image/webp")) extension = ".webp";
                else throw new Exception("Unsupported image format.");

                base64 = base64.Substring(base64.IndexOf(',') + 1);

                byte[] imageBytes = Convert.FromBase64String(base64);

                if (imageBytes.Length == 0 || imageBytes.Length > MaxImageSize)
                    throw new Exception("Image must be between 1 byte and 5 MB.");

                if (string.IsNullOrWhiteSpace(webFolder))
                    throw new Exception("Web folder is not configured.");

                string imageFolder = Path.Combine(webFolder, "Images", "products");
                Directory.CreateDirectory(imageFolder);

                string fileName = "product_" + Guid.NewGuid().ToString("N") + extension;
                File.WriteAllBytes(Path.Combine(imageFolder, fileName), imageBytes);

                return "Images/products/" + fileName;
            }
            catch (FormatException)
            {
                throw new Exception("The uploaded image data is invalid.");
            }
        }

        private void DeleteImageFile(string imagePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(imagePath) || string.IsNullOrWhiteSpace(webFolder))
                    return;

                string fullPath = Path.GetFullPath(Path.Combine(
                    webFolder, imagePath.Replace('/', Path.DirectorySeparatorChar)));

                string imageRoot = Path.GetFullPath(
                    Path.Combine(webFolder, "Images", "products"));

                if (fullPath.StartsWith(imageRoot + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }
            }
            catch { /* ignore file cleanup problems */ }
        }

        private void DeleteProduct(int id)
        {
            if (id <= 0) { Fail("deleteProduct", "Invalid product ID."); return; }

            try
            {
                string imagePath = "";

                using (MySqlConnection conn = Database.GetConnection())
                {
                    conn.Open();

                    using (MySqlCommand selectCmd = new MySqlCommand(
                        "SELECT image_path FROM products WHERE id = @id", conn))
                    {
                        selectCmd.Parameters.AddWithValue("@id", id);
                        bool found = false;

                        using (MySqlDataReader r = selectCmd.ExecuteReader())
                        {
                            if (r.Read())
                            {
                                found = true;
                                imagePath = Str(r["image_path"]);
                            }
                        }

                        if (!found)
                        {
                            Fail("deleteProduct", "Product not found.");
                            return;
                        }
                    }

                    using (MySqlCommand deleteCmd = new MySqlCommand(
                        "DELETE FROM products WHERE id = @id", conn))
                    {
                        deleteCmd.Parameters.AddWithValue("@id", id);
                        deleteCmd.ExecuteNonQuery();
                    }
                }

                DeleteImageFile(imagePath);
                Ok("deleteProduct", "Product deleted successfully!");
            }
            catch (Exception ex)
            {
                Fail("deleteProduct", "Unable to delete product: " + ex.Message);
            }
        }

        // =====================================
        // LOGIN
        // =====================================

        private void LoginUser(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                Fail("login", "Please enter your username and password.");
                return;
            }

            try
            {
                string fullname, storedPassword, role;
                int userId;

                using (MySqlConnection conn = Database.GetConnection())
                {
                    conn.Open();

                    using (MySqlCommand cmd = new MySqlCommand(
                        "SELECT id, fullname, password, role FROM users WHERE username = @u LIMIT 1", conn))
                    {
                        cmd.Parameters.AddWithValue("@u", username.Trim());

                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            if (!reader.Read())
                            {
                                Fail("login", "Invalid username or password.");
                                return;
                            }

                            userId = Convert.ToInt32(reader["id"]);
                            fullname = Str(reader["fullname"]);
                            storedPassword = Str(reader["password"]);
                            role = Str(reader["role"]).Trim().ToLower();
                        }
                    }

                    if (!PasswordMatches(password, storedPassword))
                    {
                        Fail("login", "Invalid username or password.");
                        return;
                    }

                    if (role != "admin" && role != "customer")
                    {
                        Fail("login", "Invalid account role.");
                        return;
                    }

                    // Upgrade old plain-text passwords to BCrypt.
                    if (!IsBcryptHash(storedPassword))
                    {
                        using (MySqlCommand up = new MySqlCommand(
                            "UPDATE users SET password = @p WHERE id = @id", conn))
                        {
                            up.Parameters.AddWithValue("@p", BCrypt.Net.BCrypt.HashPassword(password));
                            up.Parameters.AddWithValue("@id", userId);
                            up.ExecuteNonQuery();
                        }
                    }
                }

                loggedInRole = role;
                loggedInUserId = userId;

                SendResponse(new
                {
                    action = "login",
                    success = true,
                    username = username.Trim(),
                    fullname = fullname,
                    role = role,
                    message = "Login successful!"
                });
            }
            catch (Exception ex) { Fail("login", "Database Error: " + ex.Message); }
        }

        // =====================================
        // REGISTER
        // =====================================

        private void RegisterUser(string username, string fullname, string email,
                                  string phone, string password)
        {
            if (string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(fullname) ||
                string.IsNullOrWhiteSpace(password))
            {
                Fail("register", "Please fill up all required fields.");
                return;
            }

            if (password.Length < 8)
            {
                Fail("register", "Password must have at least 8 characters.");
                return;
            }

            if (!IsValidEmail(email))
            {
                Fail("register", "Please enter a valid email address.");
                return;
            }

            try
            {
                using (MySqlConnection conn = Database.GetConnection())
                {
                    conn.Open();

                    using (MySqlCommand check = new MySqlCommand(
                        "SELECT COUNT(*) FROM users WHERE username = @u", conn))
                    {
                        check.Parameters.AddWithValue("@u", username.Trim());

                        if (Convert.ToInt32(check.ExecuteScalar()) > 0)
                        {
                            Fail("register", "Username already exists.");
                            return;
                        }
                    }

                    string insert = @"
                        INSERT INTO users (username, fullname, email, phone, password, role)
                        VALUES (@username, @fullname, @email, @phone, @password, 'customer')";

                    using (MySqlCommand cmd = new MySqlCommand(insert, conn))
                    {
                        cmd.Parameters.AddWithValue("@username", username.Trim());
                        cmd.Parameters.AddWithValue("@fullname", fullname.Trim());
                        cmd.Parameters.AddWithValue("@email",
                            string.IsNullOrWhiteSpace(email) ? (object)DBNull.Value : email.Trim());
                        cmd.Parameters.AddWithValue("@phone",
                            string.IsNullOrWhiteSpace(phone) ? (object)DBNull.Value : phone.Trim());
                        cmd.Parameters.AddWithValue("@password", BCrypt.Net.BCrypt.HashPassword(password));

                        int result = cmd.ExecuteNonQuery();

                        if (result > 0) Ok("register", "Registration successful! You can now login.");
                        else Fail("register", "Registration failed.");
                    }
                }
            }
            catch (MySqlException ex)
            {
                Fail("register", ex.Number == 1062
                    ? "Username already exists."
                    : "Database Error: " + ex.Message);
            }
            catch (Exception ex) { Fail("register", "Registration Error: " + ex.Message); }
        }

        // =====================================
        // PROFILE / PASSWORD
        // =====================================

        private void GetProfile()
        {
            try
            {
                using (MySqlConnection conn = Database.GetConnection())
                {
                    conn.Open();

                    using (MySqlCommand cmd = new MySqlCommand(
                        "SELECT fullname, email, phone, address FROM users WHERE id = @id", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", loggedInUserId);

                        using (MySqlDataReader r = cmd.ExecuteReader())
                        {
                            if (!r.Read()) { Fail("getProfile", "Account not found."); return; }

                            SendResponse(new
                            {
                                action = "getProfile",
                                success = true,
                                data = new
                                {
                                    fullname = Str(r["fullname"]),
                                    email = Str(r["email"]),
                                    phone = Str(r["phone"]),
                                    address = Str(r["address"])
                                }
                            });
                        }
                    }
                }
            }
            catch (Exception ex) { Fail("getProfile", ex.Message); }
        }

        private void UpdateProfile(JsonElement root)
        {
            string fullname = GetString(root, "fullname").Trim();
            string email = GetString(root, "email").Trim();
            string phone = GetString(root, "phone").Trim();
            string address = GetString(root, "address").Trim();

            if (fullname == "") { Fail("updateProfile", "Full name is required."); return; }
            if (!IsValidEmail(email)) { Fail("updateProfile", "Please enter a valid email address."); return; }

            try
            {
                using (MySqlConnection conn = Database.GetConnection())
                {
                    conn.Open();

                    using (MySqlCommand cmd = new MySqlCommand(@"
                        UPDATE users
                        SET fullname = @f, email = @e, phone = @p, address = @a
                        WHERE id = @id", conn))
                    {
                        cmd.Parameters.AddWithValue("@f", fullname);
                        cmd.Parameters.AddWithValue("@e", email == "" ? (object)DBNull.Value : email);
                        cmd.Parameters.AddWithValue("@p", phone == "" ? (object)DBNull.Value : phone);
                        cmd.Parameters.AddWithValue("@a", address == "" ? (object)DBNull.Value : address);
                        cmd.Parameters.AddWithValue("@id", loggedInUserId);
                        cmd.ExecuteNonQuery();
                    }
                }

                SendResponse(new
                {
                    action = "updateProfile",
                    success = true,
                    message = "Profile updated successfully!",
                    data = new { fullname = fullname }
                });
            }
            catch (Exception ex) { Fail("updateProfile", ex.Message); }
        }

        private void ChangePassword(string currentPassword, string newPassword)
        {
            if (string.IsNullOrEmpty(currentPassword) || string.IsNullOrEmpty(newPassword))
            {
                Fail("changePassword", "Please fill up all password fields.");
                return;
            }

            if (newPassword.Length < 8)
            {
                Fail("changePassword", "New password must have at least 8 characters.");
                return;
            }

            try
            {
                using (MySqlConnection conn = Database.GetConnection())
                {
                    conn.Open();

                    string stored;
                    using (MySqlCommand cmd = new MySqlCommand(
                        "SELECT password FROM users WHERE id = @id", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", loggedInUserId);
                        object o = cmd.ExecuteScalar();

                        if (o == null || o == DBNull.Value)
                        {
                            Fail("changePassword", "Account not found.");
                            return;
                        }

                        stored = o.ToString();
                    }

                    if (!PasswordMatches(currentPassword, stored))
                    {
                        Fail("changePassword", "Current password is incorrect.");
                        return;
                    }

                    using (MySqlCommand up = new MySqlCommand(
                        "UPDATE users SET password = @p WHERE id = @id", conn))
                    {
                        up.Parameters.AddWithValue("@p", BCrypt.Net.BCrypt.HashPassword(newPassword));
                        up.Parameters.AddWithValue("@id", loggedInUserId);
                        up.ExecuteNonQuery();
                    }
                }

                Ok("changePassword", "Password updated successfully!");
            }
            catch (Exception ex) { Fail("changePassword", ex.Message); }
        }

        // =====================================
        // DASHBOARDS / REPORTS
        // =====================================

        private void GetAdminDashboard()
        {
            try
            {
                using (MySqlConnection conn = Database.GetConnection())
                {
                    conn.Open();

                    SendResponse(new
                    {
                        action = "getDashboard",
                        success = true,
                        data = new
                        {
                            totalProducts = Convert.ToInt32(Scalar(conn, "SELECT COUNT(*) FROM products")),
                            totalCustomers = Convert.ToInt32(Scalar(conn, "SELECT COUNT(*) FROM users WHERE role = 'customer'")),
                            totalOrders = Convert.ToInt32(Scalar(conn, "SELECT COUNT(*) FROM orders")),
                            totalRevenue = Convert.ToDecimal(Scalar(conn,
                                "SELECT COALESCE(SUM(total_amount), 0) FROM orders WHERE status = 'Completed'"))
                        }
                    });
                }
            }
            catch (Exception ex) { Fail("getDashboard", ex.Message); }
        }

        private void GetCustomerDashboard()
        {
            try
            {
                using (MySqlConnection conn = Database.GetConnection())
                {
                    conn.Open();

                    SendResponse(new
                    {
                        action = "getCustomerDashboard",
                        success = true,
                        data = new
                        {
                            totalProducts = Convert.ToInt32(Scalar(conn, "SELECT COUNT(*) FROM products WHERE stock > 0")),
                            totalOrders = Convert.ToInt32(Scalar(conn, "SELECT COUNT(*) FROM orders WHERE user_id = @uid", loggedInUserId)),
                            totalSpent = Convert.ToDecimal(Scalar(conn,
                                "SELECT COALESCE(SUM(total_amount), 0) FROM orders WHERE user_id = @uid AND status <> 'Cancelled'",
                                loggedInUserId))
                        }
                    });
                }
            }
            catch (Exception ex) { Fail("getCustomerDashboard", ex.Message); }
        }

        private void GetReports()
        {
            try
            {
                using (MySqlConnection conn = Database.GetConnection())
                {
                    conn.Open();

                    SendResponse(new
                    {
                        action = "getReports",
                        success = true,
                        data = new
                        {
                            totalRevenue = Convert.ToDecimal(Scalar(conn,
                                "SELECT COALESCE(SUM(total_amount), 0) FROM orders WHERE status = 'Completed'")),
                            completedOrders = Convert.ToInt32(Scalar(conn,
                                "SELECT COUNT(*) FROM orders WHERE status = 'Completed'"))
                        }
                    });
                }
            }
            catch (Exception ex) { Fail("getReports", ex.Message); }
        }

        // =====================================
        // ADMIN LISTS
        // =====================================

        private void GetCustomers()
        {
            try
            {
                using (MySqlConnection conn = Database.GetConnection())
                {
                    conn.Open();

                    using (MySqlCommand cmd = new MySqlCommand(@"
                        SELECT id, fullname, email, phone
                        FROM users WHERE role = 'customer' ORDER BY id DESC", conn))
                    using (MySqlDataReader r = cmd.ExecuteReader())
                    {
                        List<object> list = new List<object>();

                        while (r.Read())
                        {
                            list.Add(new
                            {
                                id = Convert.ToInt32(r["id"]),
                                fullname = Str(r["fullname"]),
                                email = Str(r["email"]),
                                phone = Str(r["phone"])
                            });
                        }

                        SendResponse(new { action = "getCustomers", success = true, data = list });
                    }
                }
            }
            catch (Exception ex) { Fail("getCustomers", ex.Message); }
        }

        private void GetOrders()
        {
            try
            {
                using (MySqlConnection conn = Database.GetConnection())
                {
                    conn.Open();

                    using (MySqlCommand cmd = new MySqlCommand(@"
                        SELECT o.id, u.fullname, o.order_date, o.total_amount, o.status
                        FROM orders o
                        LEFT JOIN users u ON o.user_id = u.id
                        ORDER BY o.id DESC", conn))
                    using (MySqlDataReader r = cmd.ExecuteReader())
                    {
                        List<object> list = new List<object>();

                        while (r.Read())
                        {
                            list.Add(new
                            {
                                id = Convert.ToInt32(r["id"]),
                                customer_name = r["fullname"] == DBNull.Value ? "Deleted user" : Str(r["fullname"]),
                                order_date = Fmt(r["order_date"]),
                                total_amount = Convert.ToDecimal(r["total_amount"]),
                                status = Str(r["status"])
                            });
                        }

                        SendResponse(new { action = "getOrders", success = true, data = list });
                    }
                }
            }
            catch (Exception ex) { Fail("getOrders", ex.Message); }
        }

        private void GetPayments()
        {
            try
            {
                using (MySqlConnection conn = Database.GetConnection())
                {
                    conn.Open();

                    using (MySqlCommand cmd = new MySqlCommand(@"
                        SELECT id, order_id, payment_method, amount, payment_status, payment_date
                        FROM payments ORDER BY id DESC", conn))
                    using (MySqlDataReader r = cmd.ExecuteReader())
                    {
                        List<object> list = new List<object>();

                        while (r.Read())
                        {
                            list.Add(new
                            {
                                id = Convert.ToInt32(r["id"]),
                                order_id = Convert.ToInt32(r["order_id"]),
                                payment_method = Str(r["payment_method"]),
                                amount = Convert.ToDecimal(r["amount"]),
                                payment_status = Str(r["payment_status"]),
                                payment_date = Fmt(r["payment_date"])
                            });
                        }

                        SendResponse(new { action = "getPayments", success = true, data = list });
                    }
                }
            }
            catch (Exception ex) { Fail("getPayments", ex.Message); }
        }

        // =====================================
        // CUSTOMER ORDERS
        // =====================================

        private void GetMyOrders()
        {
            try
            {
                using (MySqlConnection conn = Database.GetConnection())
                {
                    conn.Open();

                    using (MySqlCommand cmd = new MySqlCommand(@"
                        SELECT id, order_date, total_amount, status
                        FROM orders WHERE user_id = @uid ORDER BY id DESC", conn))
                    {
                        cmd.Parameters.AddWithValue("@uid", loggedInUserId);

                        using (MySqlDataReader r = cmd.ExecuteReader())
                        {
                            List<object> list = new List<object>();

                            while (r.Read())
                            {
                                list.Add(new
                                {
                                    id = Convert.ToInt32(r["id"]),
                                    order_date = Fmt(r["order_date"]),
                                    total_amount = Convert.ToDecimal(r["total_amount"]),
                                    status = Str(r["status"])
                                });
                            }

                            SendResponse(new { action = "getMyOrders", success = true, data = list });
                        }
                    }
                }
            }
            catch (Exception ex) { Fail("getMyOrders", ex.Message); }
        }

        private void GetOrderDetails(int id)
        {
            if (id <= 0) { Fail("getOrderDetails", "Invalid order ID."); return; }

            try
            {
                using (MySqlConnection conn = Database.GetConnection())
                {
                    conn.Open();

                    object order = null;

                    string sql = @"
                        SELECT o.id, o.order_date, o.total_amount, o.status, o.shipping_address,
                               p.payment_method, p.payment_status
                        FROM orders o
                        LEFT JOIN payments p ON p.order_id = o.id
                        WHERE o.id = @id" +
                        (loggedInRole == "admin" ? "" : " AND o.user_id = @uid") + " LIMIT 1";

                    using (MySqlCommand cmd = new MySqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.Parameters.AddWithValue("@uid", loggedInUserId);

                        using (MySqlDataReader r = cmd.ExecuteReader())
                        {
                            if (r.Read())
                            {
                                order = new
                                {
                                    id = Convert.ToInt32(r["id"]),
                                    order_date = Fmt(r["order_date"]),
                                    total_amount = Convert.ToDecimal(r["total_amount"]),
                                    status = Str(r["status"]),
                                    shipping_address = Str(r["shipping_address"]),
                                    payment_method = Str(r["payment_method"]),
                                    payment_status = Str(r["payment_status"])
                                };
                            }
                        }
                    }

                    if (order == null)
                    {
                        Fail("getOrderDetails", "Order not found.");
                        return;
                    }

                    List<object> items = new List<object>();

                    using (MySqlCommand cmd = new MySqlCommand(@"
                        SELECT product_name, quantity, price
                        FROM order_items WHERE order_id = @id", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);

                        using (MySqlDataReader r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                items.Add(new
                                {
                                    product_name = Str(r["product_name"]),
                                    quantity = Convert.ToInt32(r["quantity"]),
                                    price = Convert.ToDecimal(r["price"])
                                });
                            }
                        }
                    }

                    SendResponse(new
                    {
                        action = "getOrderDetails",
                        success = true,
                        data = new { order = order, items = items }
                    });
                }
            }
            catch (Exception ex) { Fail("getOrderDetails", ex.Message); }
        }

        // =====================================
        // CHECKOUT (transaction, server-side prices, stock locked)
        // =====================================


        private void Checkout(JsonElement root)
        {
            string paymentMethod = GetString(root, "payment_method");
            string address = GetString(root, "shipping_address").Trim();
            string phone = GetString(root, "contact_phone").Trim();
            string notes = GetString(root, "notes").Trim();

            if (loggedInUserId <= 0 || loggedInRole != "customer")
            {
                Fail("checkout", "Please log in using a customer account.");
                return;
            }

            if (Array.IndexOf(PaymentMethods, paymentMethod) < 0)
            {
                Fail("checkout", "Please choose a valid payment method.");
                return;
            }

            if (address == "" || phone == "")
            {
                Fail("checkout", "Delivery address and contact number are required.");
                return;
            }

            if (!root.TryGetProperty("items", out JsonElement itemsEl) ||
                itemsEl.ValueKind != JsonValueKind.Array ||
                itemsEl.GetArrayLength() == 0)
            {
                Fail("checkout", "Your cart is empty.");
                return;
            }

            Dictionary<int, int> wanted = new Dictionary<int, int>();

            foreach (JsonElement item in itemsEl.EnumerateArray())
            {
                int pid = GetInt(item, "product_id");
                int qty = GetInt(item, "quantity");

                if (pid <= 0 || qty <= 0)
                {
                    Fail("checkout", "Invalid product or quantity.");
                    return;
                }

                wanted[pid] = wanted.ContainsKey(pid)
                    ? wanted[pid] + qty
                    : qty;
            }

            try
            {
                using (MySqlConnection conn = Database.GetConnection())
                {
                    conn.Open();

                    using (MySqlTransaction tran = conn.BeginTransaction())
                    {
                        try
                        {
                            List<CartLine> lines = new List<CartLine>();
                            decimal total = 0;

                            // Get actual product prices and lock stock.
                            foreach (KeyValuePair<int, int> kv in wanted)
                            {
                                using (MySqlCommand cmd = new MySqlCommand(
                                    @"SELECT product_name, price, stock
                              FROM products
                              WHERE id = @id
                              FOR UPDATE", conn, tran))
                                {
                                    cmd.Parameters.AddWithValue("@id", kv.Key);

                                    using (MySqlDataReader reader = cmd.ExecuteReader())
                                    {
                                        if (!reader.Read())
                                            throw new Exception(
                                                "A product in your cart is unavailable.");

                                        string name = Str(reader["product_name"]);
                                        decimal price = Convert.ToDecimal(reader["price"]);
                                        int stock = Convert.ToInt32(reader["stock"]);

                                        if (stock < kv.Value)
                                            throw new Exception(
                                                "Not enough stock for " + name +
                                                ". Available: " + stock);

                                        lines.Add(new CartLine
                                        {
                                            ProductId = kv.Key,
                                            Name = name,
                                            Price = price,
                                            Quantity = kv.Value
                                        });

                                        total += price * kv.Value;
                                    }
                                }
                            }

                            // Create order.
                            long orderId;

                            using (MySqlCommand cmd = new MySqlCommand(@"
                        INSERT INTO orders
                        (
                            user_id,
                            order_date,
                            total_amount,
                            status,
                            shipping_address,
                            contact_phone,
                            notes
                        )
                        VALUES
                        (
                            @uid,
                            NOW(),
                            @total,
                            'Pending',
                            @address,
                            @phone,
                            @notes
                        )", conn, tran))
                            {
                                cmd.Parameters.AddWithValue("@uid", loggedInUserId);
                                cmd.Parameters.AddWithValue("@total", total);
                                cmd.Parameters.AddWithValue("@address", address);
                                cmd.Parameters.AddWithValue("@phone", phone);
                                cmd.Parameters.AddWithValue("@notes",
                                    notes == "" ? (object)DBNull.Value : notes);

                                cmd.ExecuteNonQuery();
                                orderId = cmd.LastInsertedId;
                            }

                            // Save individual purchased products.
                            foreach (CartLine line in lines)
                            {
                                using (MySqlCommand cmd = new MySqlCommand(@"
                            INSERT INTO order_items
                            (
                                order_id,
                                product_id,
                                product_name,
                                quantity,
                                price
                            )
                            VALUES
                            (
                                @oid,
                                @pid,
                                @name,
                                @qty,
                                @price
                            )", conn, tran))
                                {
                                    cmd.Parameters.AddWithValue("@oid", orderId);
                                    cmd.Parameters.AddWithValue("@pid", line.ProductId);
                                    cmd.Parameters.AddWithValue("@name", line.Name);
                                    cmd.Parameters.AddWithValue("@qty", line.Quantity);
                                    cmd.Parameters.AddWithValue("@price", line.Price);

                                    cmd.ExecuteNonQuery();
                                }

                                // Deduct product stock.
                                using (MySqlCommand cmd = new MySqlCommand(@"
                            UPDATE products
                            SET stock = stock - @qty
                            WHERE id = @pid AND stock >= @qty", conn, tran))
                                {
                                    cmd.Parameters.AddWithValue("@qty", line.Quantity);
                                    cmd.Parameters.AddWithValue("@pid", line.ProductId);

                                    if (cmd.ExecuteNonQuery() != 1)
                                        throw new Exception(
                                            "Unable to update product stock.");
                                }
                            }

                            // Save payment in the PAYMENTS table.

                            using (MySqlCommand cmd = new MySqlCommand(@"
    INSERT INTO payments
    (
        order_id,
        payment_method,
        amount,
        payment_status,
        payment_date
    )
    VALUES
    (
        @oid,
        @method,
        @amount,
        'Pending',
        NOW()
    )", conn, tran))
                            {
                                cmd.Parameters.AddWithValue("@oid", orderId);
                                cmd.Parameters.AddWithValue("@method", paymentMethod);
                                cmd.Parameters.AddWithValue("@amount", total);

                                cmd.ExecuteNonQuery();
                            }


                            // Save all changes together.
                            tran.Commit();

                            Ok("checkout",
                                "Order #" + orderId +
                                " placed successfully!");
                        }
                        catch
                        {
                            try { tran.Rollback(); } catch { }
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Fail("checkout", "Unable to place order: " + ex.Message);
            }
        }


        // =====================================
        // CANCEL ORDER (customer) / UPDATE STATUS (admin)
        // =====================================

        private void RestoreStock(MySqlConnection conn, MySqlTransaction tran, int orderId)
        {
            using (MySqlCommand cmd = new MySqlCommand(@"
                UPDATE products p
                JOIN order_items oi ON oi.product_id = p.id
                SET p.stock = p.stock + oi.quantity
                WHERE oi.order_id = @id", conn, tran))
            {
                cmd.Parameters.AddWithValue("@id", orderId);
                cmd.ExecuteNonQuery();
            }
        }

        private void SetPaymentStatus(MySqlConnection conn, MySqlTransaction tran,
                                      int orderId, string status)
        {
            using (MySqlCommand cmd = new MySqlCommand(
                "UPDATE payments SET payment_status = @s WHERE order_id = @id", conn, tran))
            {
                cmd.Parameters.AddWithValue("@s", status);
                cmd.Parameters.AddWithValue("@id", orderId);
                cmd.ExecuteNonQuery();
            }
        }

        private void CancelOrder(int id)
        {
            if (id <= 0) { Fail("cancelOrder", "Invalid order ID."); return; }

            try
            {
                using (MySqlConnection conn = Database.GetConnection())
                {
                    conn.Open();

                    using (MySqlTransaction tran = conn.BeginTransaction())
                    {
                        try
                        {
                            string status = null;

                            using (MySqlCommand cmd = new MySqlCommand(
                                "SELECT status FROM orders WHERE id = @id AND user_id = @uid FOR UPDATE",
                                conn, tran))
                            {
                                cmd.Parameters.AddWithValue("@id", id);
                                cmd.Parameters.AddWithValue("@uid", loggedInUserId);
                                object o = cmd.ExecuteScalar();
                                if (o != null && o != DBNull.Value) status = o.ToString();
                            }

                            if (status == null)
                            {
                                tran.Rollback();
                                Fail("cancelOrder", "Order not found.");
                                return;
                            }

                            if (!string.Equals(status, "Pending", StringComparison.OrdinalIgnoreCase))
                            {
                                tran.Rollback();
                                Fail("cancelOrder", "Only pending orders can be cancelled.");
                                return;
                            }

                            using (MySqlCommand cmd = new MySqlCommand(
                                "UPDATE orders SET status = 'Cancelled' WHERE id = @id", conn, tran))
                            {
                                cmd.Parameters.AddWithValue("@id", id);
                                cmd.ExecuteNonQuery();
                            }

                            RestoreStock(conn, tran, id);
                            SetPaymentStatus(conn, tran, id, "Cancelled");

                            tran.Commit();
                            Ok("cancelOrder", "Order #" + id + " was cancelled.");
                        }
                        catch
                        {
                            try { tran.Rollback(); } catch { }
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex) { Fail("cancelOrder", "Unable to cancel order: " + ex.Message); }
        }

        private void UpdateOrderStatus(int id, string newStatus)
        {
            if (id <= 0) { Fail("updateOrderStatus", "Invalid order ID."); return; }

            // Normalise to the exact allowed spelling.
            string status = null;
            foreach (string s in OrderStatuses)
                if (string.Equals(s, newStatus, StringComparison.OrdinalIgnoreCase)) status = s;

            if (status == null) { Fail("updateOrderStatus", "Invalid order status."); return; }

            try
            {
                using (MySqlConnection conn = Database.GetConnection())
                {
                    conn.Open();

                    using (MySqlTransaction tran = conn.BeginTransaction())
                    {
                        try
                        {
                            string current = null;

                            using (MySqlCommand cmd = new MySqlCommand(
                                "SELECT status FROM orders WHERE id = @id FOR UPDATE", conn, tran))
                            {
                                cmd.Parameters.AddWithValue("@id", id);
                                object o = cmd.ExecuteScalar();
                                if (o != null && o != DBNull.Value) current = o.ToString();
                            }

                            if (current == null)
                            {
                                tran.Rollback();
                                Fail("updateOrderStatus", "Order not found.");
                                return;
                            }

                            if (string.Equals(current, status, StringComparison.OrdinalIgnoreCase))
                            {
                                tran.Rollback();
                                Ok("updateOrderStatus", "Order status is already " + status + ".");
                                return;
                            }

                            if (string.Equals(current, "Cancelled", StringComparison.OrdinalIgnoreCase))
                            {
                                tran.Rollback();
                                Fail("updateOrderStatus", "A cancelled order cannot be changed.");
                                return;
                            }

                            using (MySqlCommand cmd = new MySqlCommand(
                                "UPDATE orders SET status = @s WHERE id = @id", conn, tran))
                            {
                                cmd.Parameters.AddWithValue("@s", status);
                                cmd.Parameters.AddWithValue("@id", id);
                                cmd.ExecuteNonQuery();
                            }

                            if (status == "Cancelled")
                            {
                                RestoreStock(conn, tran, id);
                                SetPaymentStatus(conn, tran, id, "Cancelled");
                            }
                            else if (status == "Completed")
                            {
                                SetPaymentStatus(conn, tran, id, "Paid");
                            }

                            tran.Commit();
                            Ok("updateOrderStatus", "Order #" + id + " is now " + status + ".");
                        }
                        catch
                        {
                            try { tran.Rollback(); } catch { }
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex) { Fail("updateOrderStatus", "Unable to update order: " + ex.Message); }
        }

        // =====================================
        // CLOSE WEBVIEW2
        // =====================================

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            webView?.Dispose();
            base.OnFormClosed(e);
        }
    }
}