using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.IO;
using XDevkit;
using JRPC_Client;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;

namespace Fable_II_Lua_RTE
{
    public partial class Form1 : Form
    {
        IXboxConsole jtag;
        public string currDrive;
        public string currDir;
        public string currPreview;

        private readonly HttpListener _httpListener = new HttpListener();
        private readonly string _webRoot = Path.Combine(Application.StartupPath, "Monaco");


        public Form1()
        {
            InitializeComponent();
            this.Text = string.Empty;
            this.ControlBox = false;
            this.DoubleBuffered = true;
            this.MaximizedBounds = Screen.FromHandle(this.Handle).WorkingArea;

            StartServer();
        }

        private async void StartServer()
        {
            _httpListener.Prefixes.Add("http://localhost:8080/");
            _httpListener.Start();
            Console.WriteLine("Server started at http://localhost:8080/");

            while (_httpListener.IsListening)
            {
                var context = await _httpListener.GetContextAsync();
                _ = Task.Run(() => HandleRequest(context));
            }
        }

        private void HandleRequest(HttpListenerContext context)
        {
            string urlPath = context.Request.Url.AbsolutePath.TrimStart('/');
            string requestedPath = Path.Combine(_webRoot, urlPath);

            if (urlPath.StartsWith("FableIntellisense") && Directory.Exists(requestedPath))
            {
                var files = Directory.GetFiles(requestedPath, "*.js")
                                     .Select(f => Path.GetFileName(f))
                                     .ToList();

                var directories = Directory.GetDirectories(requestedPath)
                                           .Select(d => Path.GetFileName(d))
                                           .ToList();

                var entries = files.Concat(directories).ToList();

                var json = Newtonsoft.Json.JsonConvert.SerializeObject(entries);
                byte[] jsonResponse = Encoding.UTF8.GetBytes(json);

                context.Response.ContentType = "application/json";
                context.Response.OutputStream.Write(jsonResponse, 0, jsonResponse.Length);
            }

            else if (string.IsNullOrWhiteSpace(urlPath) || urlPath == "index.html")
            {
                string indexPath = Path.Combine(_webRoot, "index.html");

                if (File.Exists(indexPath))
                {
                    byte[] fileBytes = File.ReadAllBytes(indexPath);
                    context.Response.ContentType = "text/html";
                    context.Response.OutputStream.Write(fileBytes, 0, fileBytes.Length);
                }
                else
                {
                    context.Response.StatusCode = 404;
                    byte[] message = Encoding.UTF8.GetBytes("404 - Not Found (index.html missing)");
                    context.Response.OutputStream.Write(message, 0, message.Length);
                }
            }
            else if (File.Exists(requestedPath))
            {
                byte[] fileBytes = File.ReadAllBytes(requestedPath);
                context.Response.ContentType = GetMimeType(requestedPath);
                context.Response.OutputStream.Write(fileBytes, 0, fileBytes.Length);
            }
            else
            {
                context.Response.StatusCode = 404;
                byte[] message = Encoding.UTF8.GetBytes("404 - Not Found");
                context.Response.OutputStream.Write(message, 0, message.Length);
            }

            context.Response.OutputStream.Close();
        }


        private string GetMimeType(string filePath)
        {
            return filePath.EndsWith(".js") ? "application/javascript" :
                   filePath.EndsWith(".css") ? "text/css" :
                   filePath.EndsWith(".html") ? "text/html" :
                   filePath.EndsWith(".json") ? "application/json" :
                   "application/octet-stream";
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            webView21.Source = new Uri("http://localhost:8080/");
            await webView21.EnsureCoreWebView2Async();
        }

        [DllImport("user32.DLL", EntryPoint = "ReleaseCapture")]
        private extern static void ReleaseCapture();

        [DllImport("user32.DLL", EntryPoint = "SendMessage")]
        private extern static void SendMessage(IntPtr hWnd, int wMsg, int wParam, int lParam);

        private void panelTitleBar_MouseDown(object sender, MouseEventArgs e)
        {
            ReleaseCapture();
            SendMessage(this.Handle, 0x112, 0xf012, 0);
        }

        private void Form1_Resize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Maximized)
                FormBorderStyle = FormBorderStyle.None;
            else
                FormBorderStyle = FormBorderStyle.Sizable;
        }

        private void connectBtn_Click(object sender, EventArgs e)
        {
            if (jtag.Connect(out jtag))
            {
                jtag.XNotify("Connected");
                connectBtn.Text = "Connected to Xbox!";
                connectBtn.IconColor = Color.Green;
            }
            else
            {
                connectBtn.Text = "Connection Failed!\nTry again";
                connectBtn.IconColor = Color.Red;
            }
        }

        private Dictionary<string, (List<string> dirs, List<string> files)> cachedContents = new Dictionary<string, (List<string>, List<string>)>();
        private string currentPath = string.Empty;
        private string sendFilePath = string.Empty;
        private string sendFilePathName = string.Empty;
        private Stack<string> pathHistory = new Stack<string>();
        private void selectBtn_Click(object sender, EventArgs e)
        {
            if (jtag == null || !jtag.Connect(out jtag))
            {
                MessageBox.Show("Not connected to Xbox!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            pathHistory.Clear();
            currentPath = string.Empty;
            sendFilePath = string.Empty;
            sendFilePathName = string.Empty;
            string drivesString = jtag.Drives;
            List<string> drives = drivesString.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();

            try
            {
                if (drives.Count == 0)
                {
                    MessageBox.Show("No drives found!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to retrieve drives: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            using (Form driveSelectForm = new Form())
            {
                driveSelectForm.Text = string.Empty;
                driveSelectForm.Size = new Size(500, 400);
                driveSelectForm.BackColor = Color.FromArgb(27, 27, 27);
                driveSelectForm.StartPosition = FormStartPosition.CenterParent;
                driveSelectForm.ControlBox = false;
                driveSelectForm.MinimumSize = new System.Drawing.Size(driveSelectForm.Width, driveSelectForm.Height);
                driveSelectForm.ShowInTaskbar = false;

                Panel titleBar = new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 20,
                    Padding = new Padding(0)
                };

                Label labelTitle = new Label
                {
                    Text = "Select a Drive",
                    ForeColor = Color.Gainsboro,
                    Parent = titleBar,
                    Dock = DockStyle.Left,
                    Padding = new Padding(5, 0, 0, 0),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Height = 40
                };

                Label labelTitle2 = new Label
                {
                    Text = "X",
                    ForeColor = Color.Gainsboro,
                    Cursor = Cursors.Hand,
                    Parent = titleBar,
                    Dock = DockStyle.Right,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Height = 40,
                    Width = 20
                };

                Panel listBoxContainer = new Panel
                {
                    Dock = DockStyle.Fill,
                    BorderStyle = BorderStyle.FixedSingle,
                    Padding = new Padding(1)
                };

                ListBox drivesListBox = new ListBox
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.FromArgb(30, 30, 30),
                    ForeColor = Color.Gainsboro,
                    Cursor = Cursors.Hand,
                    DrawMode = DrawMode.OwnerDrawFixed,
                    BorderStyle = BorderStyle.None
                };

                foreach (string drive in drives)
                {
                    if (drive.Contains("HDD") || drive.Contains("USB"))
                    {
                        string formattedDrive = char.ToUpper(drive[0]) + drive.Substring(1).ToLower() + ":";
                        drivesListBox.Items.Add(formattedDrive);
                    }
                }

                FontAwesome.Sharp.IconButton selectButton = new FontAwesome.Sharp.IconButton
                {
                    Text = "Select Drive",
                    Cursor = Cursors.Hand,
                    ForeColor = Color.Gainsboro,
                    Dock = DockStyle.Bottom,
                    FlatStyle = FlatStyle.Flat,
                    FlatAppearance = { BorderSize = 0 }
                };

                drivesListBox.DrawItem += (s, drawArgs) =>
                {
                    if (drawArgs.Index >= 0 && drawArgs.Index < drivesListBox.Items.Count)
                    {
                        if ((drawArgs.State & DrawItemState.Selected) == DrawItemState.Selected)
                        {
                            drawArgs.Graphics.FillRectangle(new SolidBrush(Color.Gainsboro), drawArgs.Bounds);
                            drawArgs.Graphics.DrawString(drivesListBox.Items[drawArgs.Index].ToString(), drawArgs.Font, new SolidBrush(Color.Black), drawArgs.Bounds);
                        }
                        else
                        {
                            drawArgs.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(30, 30, 30)), drawArgs.Bounds);
                            drawArgs.Graphics.DrawString(drivesListBox.Items[drawArgs.Index].ToString(), drawArgs.Font, new SolidBrush(Color.Gainsboro), drawArgs.Bounds);
                        }
                    }
                };


                drivesListBox.SelectedIndexChanged += (s, args) =>
                {
                    string selectedItem = (string)drivesListBox.SelectedItem;

                    if (selectedItem == null)
                    {
                        return;
                    }
                    else if (selectedItem.StartsWith(".."))
                    {
                        if (pathHistory.Count > 0)
                        {
                            currentPath = pathHistory.Pop();
                        }
                        else
                        {
                            driveSelectForm.Close();
                            this.BeginInvoke(new Action(() => selectBtn_Click(null, null)));
                        }
                    }
                    else if (selectedItem != null && selectedItem.StartsWith(">"))
                    {
                        pathHistory.Push(currentPath);
                        string selectedDir = selectedItem.Substring(2);
                        currentPath = selectedDir;

                        Console.WriteLine("Updated path: " + currentPath);
                    }
                    else if (selectedItem != null && selectedItem.EndsWith("lua", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine(selectedItem);
                    }
                };

                titleBar.MouseDown += (s, args) =>
                {
                    ReleaseCapture();
                    SendMessage(driveSelectForm.Handle, 0x112, 0xf012, 0);
                };

                labelTitle2.Click += (s, args) =>
                {
                    driveSelectForm.Close();
                };

                drivesListBox.DoubleClick += (s, args) =>
                {
                    selectButton.PerformClick();
                };

                selectButton.Click += (s, args) =>
                {
                    string selectedDrive = (string)drivesListBox.SelectedItem;

                    if (string.IsNullOrEmpty(selectedDrive))
                    {
                        if (labelTitle.Text.EndsWith("Drive"))
                        {
                            MessageBox.Show("Please select a drive first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }
                        else
                        {
                            MessageBox.Show("Please select a file first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }
                    }

                    labelTitle.Text = "Select a File";
                    selectButton.Text = "Select File";

                    if (selectedDrive.EndsWith("lua", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            string tempFilePath = "temp.lua";
                            Console.WriteLine($"Attempting to receive Lua file from: {selectedDrive}");
                            jtag.ReceiveFile(tempFilePath, selectedDrive);
                            if (File.Exists(tempFilePath))
                            {
                                string fileName = Path.GetFileName(selectedDrive);
                                Console.WriteLine("File received successfully. Reading file content.");

                                string fileContent = File.ReadAllText(tempFilePath);
                                string jsCode = $@"var editor = monaco.editor.getModels()[0]; editor.setValue({JavaScriptEscape(fileContent)});";
                                webView21.CoreWebView2.ExecuteScriptAsync(jsCode);
                                sendFilePath = currentPath;
                                sendFilePathName = fileName;
                                driveSelectForm.Close();
                            }
                            else
                            {
                                Console.WriteLine("Failed to find the downloaded file.");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error reading Lua file: {ex.Message}");
                            Console.WriteLine($"Exception Details: {ex.ToString()}");
                            Console.WriteLine($"Reading from path: {selectedDrive}");
                        }
                        finally
                        {
                            string tempFilePath = "temp.lua";
                            if (File.Exists(tempFilePath))
                            {
                                File.Delete(tempFilePath);
                                Console.WriteLine("Temporary file deleted.");
                            }
                        }
                        return;
                    }

                    if (string.IsNullOrEmpty(currentPath))
                    {
                        currDrive = selectedDrive;
                        currDir = "\\";
                        currentPath = currDrive + currDir;
                        Console.WriteLine("Initial path: " + currentPath);
                    }
                    else
                    {
                        Console.WriteLine("Retaining currentPath: " + currentPath);
                    }
                        LoadDirectoryContents(currentPath, drivesListBox);
                };

                driveSelectForm.Controls.Add(drivesListBox);
                driveSelectForm.Controls.Add(listBoxContainer);
                driveSelectForm.Controls.Add(titleBar);
                driveSelectForm.Controls.Add(selectButton);
                driveSelectForm.ShowDialog();
            }
        }

        private static string JavaScriptEscape(string content)
        {
            return "\"" + content.Replace("\\", "\\\\")
                                  .Replace("\"", "\\\"")
                                  .Replace("\r", "\\r")
                                  .Replace("\n", "\\n") + "\"";
        }

        private void LoadDirectoryContents(string path, ListBox drivesListBox)
        {
            if (cachedContents.ContainsKey(path))
            {
                var cached = cachedContents[path];
                DisplayDirectoryContents(cached.dirs, cached.files, drivesListBox);
            }
            else
            {
                try
                {
                    IXboxFiles files = jtag.DirectoryFiles(path);
                    List<string> fileNames = new List<string>();
                    List<string> dirNames = new List<string>();

                    foreach (IXboxFile file in files)
                    {
                        if (file.IsDirectory)
                        {
                            dirNames.Add("> " + file.Name);
                        }
                        else if (file.Name.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                        {
                            fileNames.Add(file.Name);
                        }
                    }

                    cachedContents[path] = (dirNames, fileNames);
                    DisplayDirectoryContents(dirNames, fileNames, drivesListBox);
                }
                catch (COMException comEx)
                {
                    MessageBox.Show("COM Exception: " + comEx.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error loading directory contents: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
        private void DisplayDirectoryContents(List<string> dirNames, List<string> fileNames, ListBox drivesListBox)
        {
            dirNames.Sort();
            fileNames.Sort();
            drivesListBox.Items.Clear();
            drivesListBox.Items.Add(".. Go Back");

            foreach (string dir in dirNames)
            {
                drivesListBox.Items.Add(dir);
            }

            foreach (string file in fileNames)
            {
                drivesListBox.Items.Add(file);
            }
        }

        private async void iconButton3_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(sendFilePath))
            {
                Console.WriteLine("empty: " + sendFilePath);
            }
            else
            {
                string jsCode = "monaco.editor.getModels()[0].getValue();";

                string content = await webView21.CoreWebView2.ExecuteScriptAsync(jsCode);

                content = Newtonsoft.Json.JsonConvert.DeserializeObject<string>(content);

                string appFolder = Application.StartupPath;

                string tempFolderPath = Path.Combine(appFolder, "Monaco", "vs", "editor", "temp");
                Directory.CreateDirectory(tempFolderPath);

                string filePath = Path.Combine(tempFolderPath, sendFilePathName);
                File.WriteAllText(filePath, content);

                string sendIt = sendFilePath + "\\" + sendFilePathName;

                jtag.SendFile(filePath, sendIt);

                MessageBox.Show($"Code saved successfully at:\n{filePath}");
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            _httpListener.Stop();
            _httpListener.Close();
        }

        private void label2_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void iconButton6_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void iconButton5_Click(object sender, EventArgs e)
        {
            Form2 secondForm = new Form2();
            secondForm.Text = string.Empty;
            secondForm.StartPosition = FormStartPosition.CenterParent;
            secondForm.ShowInTaskbar = false;
            secondForm.ShowDialog();
        }

        private void iconButton4_Click(object sender, EventArgs e)
        {
            MessageBox.Show($"Not coded yet!");
        }
    }
}
