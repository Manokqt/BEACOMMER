
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

        private const int MaxImageSize = 5 * 1024 * 1024;

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
                            "..",
                            "..",
                            "..",
                            "Web",
                            "index.html"
                        )
                    );

                    if (File.Exists(projectPath))
                    {
                        htmlPath = projectPath;
                    }
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
                MessageBox.Show(
                    "Response Error: " + ex.Message,
                    "BEACOMMER"
                );
            }
        }

        // =====================================
        // JSON HELPERS
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

        // =====================================
        // CHECK BCRYPT FORMAT
        // =====================================

        private bool IsBcryptHash(string password)
        {
            return password.StartsWith("$2a$") ||
                   password.StartsWith("$2b$") ||
                   password.StartsWith("$2y$");
        }

        // =====================================
        // ADMIN ACCESS CHECK
        // =====================================

        private bool RequireAdmin(string action)
        {
            if (loggedInRole != "admin")
            {
                SendResponse(new
                {
                    action = action,
                    success = false,
                    message = "Administrator access is required."
                });

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

                        using (JsonDocument innerDoc =
                            JsonDocument.Parse(message))
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
                SendResponse(new
                {
                    action = "error",
                    success = false,
                    message = "Application Error: " + ex.Message
                });
            }
        }

        private void HandleAction(JsonElement root)
        {
            string action = GetString(root, "action");

            if (string.IsNullOrWhiteSpace(action))
            {
                SendResponse(new
                {
                    action = "error",
                    success = false,
                    message = "Missing action."
                });

                return;
            }

            switch (action)
            {
                case "login":
                    LoginUser(
                        GetString(root, "username"),
                        GetString(root, "password")
                    );
                    break;

                case "register":
                    RegisterUser(
                        GetString(root, "username"),
                        GetString(root, "fullname"),
                        GetString(root, "password")
                    );
                    break;

                case "logout":
                    loggedInRole = "";

                    SendResponse(new
                    {
                        action = "logout",
                        success = true,
                        message = "Successfully logged out."
                    });
                    break;

                case "getCategories":
                    if (RequireAdmin(action))
                        GetCategories();
                    break;

                case "addCategory":
                    if (RequireAdmin(action))
                        AddCategory(
                            GetString(root, "category_name")
                        );
                    break;

                case "deleteCategory":
                    if (RequireAdmin(action))
                        DeleteCategory(
                            GetInt(root, "id")
                        );
                    break;

                case "getProducts":
                    GetProducts();
                    break;

                case "addProduct":
                    if (RequireAdmin(action))
                        AddProduct(root);
                    break;

                case "deleteProduct":
                    if (RequireAdmin(action))
                        DeleteProduct(
                            GetInt(root, "id")
                        );
                    break;

                default:
                    SendResponse(new
                    {
                        action = action,
                        success = false,
                        message = "Action is not yet implemented: " + action
                    });
                    break;
            }
        }

        // =====================================
        // GET CATEGORIES
        // =====================================

        private void GetCategories()
        {
            try
            {
                using (MySqlConnection conn = Database.GetConnection())
                {
                    conn.Open();

                    string query = @"
                        SELECT id, category_name
                        FROM categories
                        ORDER BY category_name";

                    using (MySqlCommand cmd =
                        new MySqlCommand(query, conn))
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        List<object> categories = new List<object>();

                        while (reader.Read())
                        {
                            categories.Add(new
                            {
                                id = Convert.ToInt32(reader["id"]),
                                category_name =
                                    reader["category_name"].ToString()
                            });
                        }

                        SendResponse(new
                        {
                            action = "getCategories",
                            success = true,
                            data = categories
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                SendResponse(new
                {
                    action = "getCategories",
                    success = false,
                    message = ex.Message
                });
            }
        }

        // =====================================
        // ADD CATEGORY
        // =====================================

        private void AddCategory(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName))
            {
                SendResponse(new
                {
                    action = "addCategory",
                    success = false,
                    message = "Please enter a category name."
                });

                return;
            }

            try
            {
                using (MySqlConnection conn = Database.GetConnection())
                {
                    conn.Open();

                    string query = @"
                        INSERT INTO categories (category_name)
                        VALUES (@category_name)";

                    using (MySqlCommand cmd =
                        new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue(
                            "@category_name",
                            categoryName.Trim()
                        );

                        cmd.ExecuteNonQuery();
                    }
                }

                SendResponse(new
                {
                    action = "addCategory",
                    success = true,
                    message = "Category added successfully!"
                });
            }
            catch (MySqlException ex)
            {
                SendResponse(new
                {
                    action = "addCategory",
                    success = false,
                    message = ex.Number == 1062
                        ? "This category already exists."
                        : ex.Message
                });
            }
            catch (Exception ex)
            {
                SendResponse(new
                {
                    action = "addCategory",
                    success = false,
                    message = ex.Message
                });
            }
        }

        // =====================================
        // DELETE CATEGORY
        // =====================================

        private void DeleteCategory(int id)
        {
            if (id <= 0)
                return;

            try
            {
                using (MySqlConnection conn = Database.GetConnection())
                {
                    conn.Open();

                    string query = @"
                        DELETE FROM categories
                        WHERE id = @id";

                    using (MySqlCommand cmd =
                        new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);

                        int result = cmd.ExecuteNonQuery();

                        SendResponse(new
                        {
                            action = "deleteCategory",
                            success = result > 0,
                            message = result > 0
                                ? "Category deleted successfully!"
                                : "Category was not found."
                        });
                    }
                }
            }
            catch (MySqlException ex)
            {
                SendResponse(new
                {
                    action = "deleteCategory",
                    success = false,
                    message = "Cannot delete this category. It may still be used by products. " +
                              ex.Message
                });
            }
            catch (Exception ex)
            {
                SendResponse(new
                {
                    action = "deleteCategory",
                    success = false,
                    message = ex.Message
                });
            }
        }

        // =====================================
        // GET PRODUCTS
        // =====================================

        private void GetProducts()
        {
            try
            {
                using (MySqlConnection conn = Database.GetConnection())
                {
                    conn.Open();

                    string query = @"
                        SELECT
                            p.id,
                            p.product_name,
                            p.category_id,
                            c.category_name,
                            p.description,
                            p.price,
                            p.stock,
                            p.image_path
                        FROM products p
                        LEFT JOIN categories c
                            ON p.category_id = c.id
                        ORDER BY p.id DESC";

                    using (MySqlCommand cmd =
                        new MySqlCommand(query, conn))
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        List<object> products = new List<object>();

                        while (reader.Read())
                        {
                            products.Add(new
                            {
                                id = Convert.ToInt32(reader["id"]),
                                product_name = reader["product_name"].ToString(),
                                category_id = Convert.ToInt32(reader["category_id"]),
                                category_name = reader["category_name"] == DBNull.Value
                                    ? "Uncategorized"
                                    : reader["category_name"].ToString(),
                                description = reader["description"] == DBNull.Value
                                    ? ""
                                    : reader["description"].ToString(),
                                price = Convert.ToDecimal(reader["price"]),
                                stock = Convert.ToInt32(reader["stock"]),
                                image_path = reader["image_path"] == DBNull.Value
                                    ? ""
                                    : reader["image_path"].ToString()
                            });
                        }

                        SendResponse(new
                        {
                            action = "getProducts",
                            success = true,
                            data = products
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                SendResponse(new
                {
                    action = "getProducts",
                    success = false,
                    message = ex.Message
                });
            }
        }

        // =====================================
        // ADD PRODUCT WITH IMAGE
        // =====================================

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
                SendResponse(new
                {
                    action = "addProduct",
                    success = false,
                    message = "Please enter a product name."
                });
                return;
            }

            if (categoryId <= 0 || price < 0 || stock < 0)
            {
                SendResponse(new
                {
                    action = "addProduct",
                    success = false,
                    message = "Please check the category, price, and stock."
                });
                return;
            }

            string savedImagePath = "";

            try
            {
                // CHECK CATEGORY
                using (MySqlConnection conn = Database.GetConnection())
                {
                    conn.Open();

                    string checkCategory = @"
                        SELECT COUNT(*)
                        FROM categories
                        WHERE id = @id";

                    using (MySqlCommand checkCmd =
                        new MySqlCommand(checkCategory, conn))
                    {
                        checkCmd.Parameters.AddWithValue(
                            "@id",
                            categoryId
                        );

                        if (Convert.ToInt32(checkCmd.ExecuteScalar()) == 0)
                        {
                            SendResponse(new
                            {
                                action = "addProduct",
                                success = false,
                                message = "Selected category does not exist."
                            });
                            return;
                        }
                    }
                }

                // SAVE IMAGE IF SELECTED
                if (!string.IsNullOrWhiteSpace(imageBase64))
                {
                    savedImagePath = SaveProductImage(imageBase64);
                }

                // INSERT PRODUCT
                using (MySqlConnection conn = Database.GetConnection())
                {
                    conn.Open();

                    string query = @"
                        INSERT INTO products
                        (
                            product_name,
                            category_id,
                            description,
                            price,
                            stock,
                            image_path
                        )
                        VALUES
                        (
                            @product_name,
                            @category_id,
                            @description,
                            @price,
                            @stock,
                            @image_path
                        )";

                    using (MySqlCommand cmd =
                        new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue(
                            "@product_name",
                            productName.Trim()
                        );

                        cmd.Parameters.AddWithValue(
                            "@category_id",
                            categoryId
                        );

                        cmd.Parameters.AddWithValue(
                            "@description",
                            description
                        );

                        cmd.Parameters.AddWithValue(
                            "@price",
                            price
                        );

                        cmd.Parameters.AddWithValue(
                            "@stock",
                            stock
                        );

                        cmd.Parameters.AddWithValue(
                            "@image_path",
                            string.IsNullOrEmpty(savedImagePath)
                                ? (object)DBNull.Value
                                : savedImagePath
                        );

                        int result = cmd.ExecuteNonQuery();

                        if (result <= 0)
                        {
                            throw new Exception("Product was not inserted.");
                        }
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
                SendResponse(new
                {
                    action = "addProduct",
                    success = false,
                    message = "Unable to save product: " + ex.Message
                });
            }
        }

        // =====================================
        // SAVE PRODUCT IMAGE
        // =====================================

        private string SaveProductImage(string base64Data)
        {
            try
            {
                string base64 = base64Data;
                string extension = "";

                // Example:
                // data:image/png;base64,AAAA...

                if (base64.Contains(","))
                {
                    string header = base64.Substring(
                        0,
                        base64.IndexOf(',')
                    );

                    if (header.Contains("image/png"))
                        extension = ".png";
                    else if (header.Contains("image/jpeg"))
                        extension = ".jpg";
                    else if (header.Contains("image/webp"))
                        extension = ".webp";
                    else
                        throw new Exception("Unsupported image format.");

                    base64 = base64.Substring(
                        base64.IndexOf(',') + 1
                    );
                }
                else
                {
                    throw new Exception("Invalid image data.");
                }

                byte[] imageBytes = Convert.FromBase64String(base64);

                if (imageBytes.Length == 0 ||
                    imageBytes.Length > MaxImageSize)
                {
                    throw new Exception("Image must be between 1 byte and 5 MB.");
                }

                if (string.IsNullOrWhiteSpace(webFolder))
                {
                    throw new Exception("Web folder is not configured.");
                }

                string imageFolder = Path.Combine(
                    webFolder,
                    "Images",
                    "products"
                );

                if (!Directory.Exists(imageFolder))
                {
                    Directory.CreateDirectory(imageFolder);
                }

                string fileName =
                    "product_" + Guid.NewGuid().ToString("N") + extension;

                string fullPath = Path.Combine(
                    imageFolder,
                    fileName
                );

                File.WriteAllBytes(fullPath, imageBytes);

                // Relative path used by index.html
                return "Images/products/" + fileName;
            }
            catch (FormatException)
            {
                throw new Exception("The uploaded image data is invalid.");
            }
        }

        // =====================================
        // DELETE PRODUCT
        // =====================================

        private void DeleteProduct(int id)
        {
            if (id <= 0)
            {
                SendResponse(new
                {
                    action = "deleteProduct",
                    success = false,
                    message = "Invalid product ID."
                });
                return;
            }

            try
            {
                string imagePath = "";

                using (MySqlConnection conn = Database.GetConnection())
                {
                    conn.Open();

                    string selectQuery = @"
                        SELECT image_path
                        FROM products
                        WHERE id = @id";

                    using (MySqlCommand selectCmd =
                        new MySqlCommand(selectQuery, conn))
                    {
                        selectCmd.Parameters.AddWithValue("@id", id);

                        object result = selectCmd.ExecuteScalar();

                        if (result == null || result == DBNull.Value)
                        {
                            SendResponse(new
                            {
                                action = "deleteProduct",
                                success = false,
                                message = "Product not found."
                            });
                            return;
                        }

                        imagePath = result.ToString();
                    }

                    string deleteQuery = @"
                        DELETE FROM products
                        WHERE id = @id";

                    using (MySqlCommand deleteCmd =
                        new MySqlCommand(deleteQuery, conn))
                    {
                        deleteCmd.Parameters.AddWithValue("@id", id);

                        deleteCmd.ExecuteNonQuery();
                    }
                }

                // Delete image file after successful database deletion.
                if (!string.IsNullOrWhiteSpace(imagePath))
                {
                    string fullPath = Path.Combine(
                        webFolder,
                        imagePath.Replace(
                            '/',
                            Path.DirectorySeparatorChar
                        )
                    );

                    string imageRoot = Path.GetFullPath(
                        Path.Combine(webFolder, "Images", "products")
                    );

                    string safePath = Path.GetFullPath(fullPath);

                    if (safePath.StartsWith(
                        imageRoot + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase
                    ) && File.Exists(safePath))
                    {
                        File.Delete(safePath);
                    }
                }

                SendResponse(new
                {
                    action = "deleteProduct",
                    success = true,
                    message = "Product deleted successfully!"
                });
            }
            catch (Exception ex)
            {
                SendResponse(new
                {
                    action = "deleteProduct",
                    success = false,
                    message = "Unable to delete product: " + ex.Message
                });
            }
        }

        // =====================================
        // LOGIN FUNCTION
        // =====================================

        private void LoginUser(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(password))
            {
                SendResponse(new
                {
                    action = "login",
                    success = false,
                    message = "Please enter your username and password."
                });
                return;
            }

            try
            {
                string fullname = "";
                string storedPassword = "";
                string role = "";
                int userId = 0;

                using (MySqlConnection conn = Database.GetConnection())
                {
                    conn.Open();

                    string query = @"
                        SELECT id, fullname, password, role
                        FROM users
                        WHERE username = @username
                        LIMIT 1";

                    using (MySqlCommand cmd =
                        new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue(
                            "@username",
                            username.Trim()
                        );

                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            if (!reader.Read())
                            {
                                SendResponse(new
                                {
                                    action = "login",
                                    success = false,
                                    message = "Invalid username or password."
                                });
                                return;
                            }

                            userId = Convert.ToInt32(reader["id"]);
                            fullname = reader["fullname"].ToString();
                            storedPassword = reader["password"].ToString();
                            role = reader["role"].ToString()
                                .Trim()
                                .ToLower();
                        }
                    }

                    bool validPassword = false;
                    bool upgradePassword = false;

                    if (IsBcryptHash(storedPassword))
                    {
                        try
                        {
                            validPassword = BCrypt.Net.BCrypt.Verify(
                                password,
                                storedPassword
                            );
                        }
                        catch
                        {
                            validPassword = false;
                        }
                    }
                    else
                    {
                        validPassword = string.Equals(
                            password,
                            storedPassword
                        );

                        upgradePassword = validPassword;
                    }

                    if (!validPassword)
                    {
                        SendResponse(new
                        {
                            action = "login",
                            success = false,
                            message = "Invalid username or password."
                        });
                        return;
                    }

                    if (upgradePassword)
                    {
                        string newHash =
                            BCrypt.Net.BCrypt.HashPassword(password);

                        string updateQuery = @"
                            UPDATE users
                            SET password = @password
                            WHERE id = @id";

                        using (MySqlCommand updateCmd =
                            new MySqlCommand(updateQuery, conn))
                        {
                            updateCmd.Parameters.AddWithValue(
                                "@password",
                                newHash
                            );

                            updateCmd.Parameters.AddWithValue(
                                "@id",
                                userId
                            );

                            updateCmd.ExecuteNonQuery();
                        }
                    }

                    if (role != "admin" && role != "customer")
                    {
                        SendResponse(new
                        {
                            action = "login",
                            success = false,
                            message = "Invalid account role."
                        });
                        return;
                    }
                }

                loggedInRole = role;

                SendResponse(new
                {
                    action = "login",
                    success = true,
                    username = username,
                    fullname = fullname,
                    role = role,
                    message = "Login successful!"
                });
            }
            catch (Exception ex)
            {
                SendResponse(new
                {
                    action = "login",
                    success = false,
                    message = "Database Error: " + ex.Message
                });
            }
        }

        // =====================================
        // REGISTRATION FUNCTION
        // =====================================

        private void RegisterUser(
            string username,
            string fullname,
            string password)
        {
            if (string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(fullname) ||
                string.IsNullOrWhiteSpace(password))
            {
                SendResponse(new
                {
                    action = "register",
                    success = false,
                    message = "Please fill up all fields."
                });
                return;
            }

            if (password.Length < 8)
            {
                SendResponse(new
                {
                    action = "register",
                    success = false,
                    message = "Password must have at least 8 characters."
                });
                return;
            }

            try
            {
                using (MySqlConnection conn = Database.GetConnection())
                {
                    conn.Open();

                    string checkQuery = @"
                        SELECT COUNT(*)
                        FROM users
                        WHERE username = @username";

                    using (MySqlCommand checkCmd =
                        new MySqlCommand(checkQuery, conn))
                    {
                        checkCmd.Parameters.AddWithValue(
                            "@username",
                            username.Trim()
                        );

                        int count = Convert.ToInt32(
                            checkCmd.ExecuteScalar()
                        );

                        if (count > 0)
                        {
                            SendResponse(new
                            {
                                action = "register",
                                success = false,
                                message = "Username already exists."
                            });
                            return;
                        }
                    }

                    string hashedPassword =
                        BCrypt.Net.BCrypt.HashPassword(password);

                    string insertQuery = @"
                        INSERT INTO users
                        (username, fullname, password, role)
                        VALUES
                        (@username, @fullname, @password, 'customer')";

                    using (MySqlCommand cmd =
                        new MySqlCommand(insertQuery, conn))
                    {
                        cmd.Parameters.AddWithValue(
                            "@username",
                            username.Trim()
                        );

                        cmd.Parameters.AddWithValue(
                            "@fullname",
                            fullname.Trim()
                        );

                        cmd.Parameters.AddWithValue(
                            "@password",
                            hashedPassword
                        );

                        int result = cmd.ExecuteNonQuery();

                        SendResponse(new
                        {
                            action = "register",
                            success = result > 0,
                            message = result > 0
                                ? "Registration successful! You can now login."
                                : "Registration failed."
                        });
                    }
                }
            }
            catch (MySqlException ex)
            {
                SendResponse(new
                {
                    action = "register",
                    success = false,
                    message = ex.Number == 1062
                        ? "Username already exists."
                        : "Database Error: " + ex.Message
                });
            }
            catch (Exception ex)
            {
                SendResponse(new
                {
                    action = "register",
                    success = false,
                    message = "Registration Error: " + ex.Message
                });
            }
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
