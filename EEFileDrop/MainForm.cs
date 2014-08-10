using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using BitSend;
using EEFileDrop.Properties;
using PlayerIOClient;
using Rabbit;

namespace EEFileDrop
{
    public partial class MainForm : Form
    {
        private static readonly byte[] _fileNamePrefix = {0x00};
        private static readonly byte[] _fileDataPrefix = {0x01};
        private readonly Dictionary<int, ListViewItem> _filesInProgress = new Dictionary<int, ListViewItem>();
        private readonly IconListManager _iconListManager;
        private readonly ImageList _largeImageList = new ImageList();
        private readonly ImageList _smallImageList = new ImageList();

        private readonly Dictionary<int, string> _usernames = new Dictionary<int, string>();
        private BitSendClient _bitSend;
        private Connection _con;

        private byte[] _file;
        private string _fileName;
        private int _ownId;

        public MainForm()
        {
            this.InitializeComponent();

            this.listViewFiles.View = Settings.Default.View;
            this.OnViewUpdate();

            this._smallImageList.ColorDepth = ColorDepth.Depth32Bit;
            this._largeImageList.ColorDepth = ColorDepth.Depth32Bit;

            this._smallImageList.ImageSize = new Size(16, 16);
            this._largeImageList.ImageSize = new Size(32, 32);

            this._iconListManager = new IconListManager(this._smallImageList, this._largeImageList);

            this.listViewFiles.SmallImageList = this._smallImageList;
            this.listViewFiles.LargeImageList = this._largeImageList;
        }

        private bool Connected
        {
            get { return this._con != null && this._con.Connected && this._bitSend != null; }
        }

        private bool CanCancel {
            get { return this.backgroundWorkerSender.IsBusy && !this.backgroundWorkerSender.CancellationPending; }
        }

        private void buttonUpload_Click(object sender, EventArgs e)
        {
            if (this.backgroundWorkerSender.IsBusy)
            {
                MessageBox.Show(this, "Another upload is currently in progress!");
                return;
            }

            this._fileName = this.openFileDialog.SafeFileName ?? String.Empty;
            if (!String.IsNullOrEmpty(this._fileName))
            {
                this._file = File.ReadAllBytes(this.openFileDialog.FileName);
                this.toolStripProgressBar.Value = 0;
                this.toolStripProgressBar.Visible = true;
                this.SetStatus("Upload in progress...", true);
                this.backgroundWorkerSender.RunWorkerAsync();

                this.openFileDialog.FileName = null;
                this.labelName.Text = "No file selected.";
                this.OnUploadStatusChange();
            }
            else
            {
                MessageBox.Show(this, "No file was selected.");
            }
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void connectToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (new ConnectForm().ShowDialog(this) == DialogResult.OK)
            {
                this.SetStatus("Connecting...", true);
                this.backgroundWorkerLogin.RunWorkerAsync();
                this.OnConnectivityChange();
            }
        }

        private void _bitSend_Status(int current, int total)
        {
            this.BeginInvoke(new Action(() =>
            {
                var statusText = String.Format("Upload in progress... ({0}/{1})", current, total);
                this.SetStatus(statusText, true);

                if (current % 50 != 0) return;

                this.toolStripProgressBar.Maximum = total;
                this.toolStripProgressBar.Value = current;
            }));
        }

        private void _bitSend_Message(int userId, byte[] data)
        {
            this.BeginInvoke(new Action(() =>
            {
                var username = this.GetUsername(userId);

                byte[] bytes = data.Skip(1).ToArray();
                if (data.First() == _fileNamePrefix.First())
                {
                    string fileName = Encoding.UTF8.GetString(bytes);
                    ListViewItem item = this.listViewFiles.Items.Add("[In progress] " + fileName + " by " + username,
                        this._iconListManager.AddFileIcon(fileName));
                    item.Tag = new FileData(fileName);
                    item.ToolTipText = "By " + username;
                    this._filesInProgress[userId] = item;
                }
                else if (data.First() == _fileDataPrefix.First())
                {
                    bytes = Compression.Decompress(bytes);

                    if (!this._filesInProgress.ContainsKey(userId)) return;
                    ListViewItem item = this._filesInProgress[userId];
                    var fileData = (FileData)item.Tag;
                    item.Text = fileData.Name + " (" + BytesToString(bytes.Length) + ")" + " by " + username;
                    fileData.Bytes = bytes;
                }
            }));
        }

        void _bitSend_Add(int userId)
        {
            this.BeginInvoke(new Action(() =>
            {
                var username = this.GetUsername(userId);
                this.listBoxUsers.Items.Add(new UserData(userId, username));
            }));
        }
        
        private void _bitSend_Remove(int userId)
        {
            this.BeginInvoke(new Action(() =>
            {
                foreach (UserData user in this.listBoxUsers.Items)
                {
                    if (user.UserId == userId)
                    {
                        this.listBoxUsers.Items.Remove(user);
                        break;
                    }
                }

                if (!this._filesInProgress.ContainsKey(userId)) return;
                ListViewItem item = this._filesInProgress[userId];
                this._filesInProgress.Remove(userId);

                item.Text = "[Cancelled] " + item.Text.Substring(14);
            }));
        }

        private void disconnectToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this._con != null)
                this._con.Disconnect();
            this._con = null;

            this.SetStatus("Disconnected.");
            this.OnConnectivityChange();
        }

        private void timerStatusReset_Tick(object sender, EventArgs e)
        {
            this.SetStatus("Ready.", true);
        }

        private void buttonSelect_Click(object sender, EventArgs e)
        {
            if (this.openFileDialog.ShowDialog(this) == DialogResult.OK)
            {
                this.labelName.Text = this.openFileDialog.SafeFileName;
            }
            this.OnUploadStatusChange();
        }

        private void OnConnectivityChange()
        {
            bool connected = (this._con != null && this._con.Connected) || this.backgroundWorkerLogin.IsBusy;

            this.connectToolStripMenuItem.Enabled = !connected;
            this.disconnectToolStripMenuItem.Enabled = connected;
            this.buttonSelect.Enabled = this.Connected;

            this.OnUploadStatusChange();
        }

        private void OnUploadStatusChange()
        {
            this.buttonUpload.Enabled =
                !String.IsNullOrEmpty(this.openFileDialog.SafeFileName) &&
                this.Connected;
            
            this.buttonUpload.Visible = !this.CanCancel;
            this.buttonCancel.Visible = this.CanCancel;
        }

        public string GetUsername(int userId)
        {
            string username = "<Unknown>";
            if (this._usernames.ContainsKey(userId))
                username = this._usernames[userId];
            return username;
        }

        private void SetStatus(string text, bool isBusy = false)
        {
            this.toolStripStatus.Text = text;
            this.timerStatusReset.Enabled = !isBusy;
        }

        private bool Send(byte[] bytes)
        {
            if (this._bitSend.Send(bytes))
            {
                this._bitSend_Message(this._ownId, bytes);
                return true;
            }
            return false;
        }

        private void backgroundWorkerSender_DoWork(object sender, DoWorkEventArgs e)
        {
            var chunks = new[]
            {
                _fileNamePrefix.Concat(Encoding.UTF8.GetBytes(this._fileName)).ToArray(),
                _fileDataPrefix.Concat(Compression.Compress(this._file)).ToArray()
            };

            if (chunks.Any(chunk => this.backgroundWorkerSender.CancellationPending || 
                !this.Send(chunk)))
            {
                e.Cancel = true;
            }
        }

        private void backgroundWorkerLogin_DoWork(object sender, DoWorkEventArgs e)
        {
            try
            {
                this._con = new RabbitAuth().LogOn(Settings.Default.Email, Settings.Default.WorldId,
                    Settings.Default.Password);
            }
            catch (PlayerIOError ex)
            {
                MessageBox.Show(ex.Message);
                return;
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(
                    "Unable to connect: Unable to detect your account type. (Make sure all fields are filled correctly)\nThe error message was: " +
                    ex.Message);
                return;
            }

            this._con.OnDisconnect += (o, message) =>
            {
                foreach (var fileSender in _filesInProgress.Keys.ToArray())
                {
                    this._bitSend_Remove(fileSender);
                }

                this._usernames.Clear();
                this.OnConnectivityChange();
            };
            this._con.OnMessage += (o, message) =>
            {
                if (message.Type == "init")
                {
                    this._ownId = message.GetInt(6);
                    string ownUsername = message.GetString(9);
                    this._usernames.Add(this._ownId, ownUsername);
                    this._bitSend = new BitSendClient(this._con, this._ownId);
                    this._bitSend.Message += this._bitSend_Message;
                    this._bitSend.Add += _bitSend_Add;
                    this._bitSend.Remove += this._bitSend_Remove;
                    this._bitSend.Status += this._bitSend_Status;

                    this.BeginInvoke(new Action(() =>
                    {
                        this.listBoxUsers.Items.Add(new UserData(_ownId, ownUsername + " (You)"));

                        this.SetStatus("Connected as " + ownUsername + ".");
                        this.OnConnectivityChange();
                    }));

                    this._con.Send("init2");
                }
                else if (message.Type == "add")
                {
                    this._usernames.Add(message.GetInt(0), message.GetString(1));
                }
                else if (message.Type == "left")
                {
                    this._usernames.Remove(message.GetInt(0));
                }
            };
            this._con.Send("init");
        }
        
        private void backgroundWorkerSender_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            this.SetStatus("Upload " + (e.Cancelled
                ? "cancelled."
                : "finished."));
            this.OnUploadStatusChange();
            this.toolStripProgressBar.Visible = false;
        }

        private void listViewFiles_SelectedIndexChanged(object sender, EventArgs e)
        {
            bool hasSelection = this.listViewFiles.SelectedItems.Count > 0;

            this.buttonRemove.Enabled = hasSelection;
            this.buttonSave.Enabled = hasSelection;
        }

        private void buttonRemove_Click(object sender, EventArgs e)
        {
            if (
                MessageBox.Show(this, "Are you sure you want to remove the selected files?", "Confirm",
                    MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                // Remove from the downloading list
                foreach (var kv in this._filesInProgress.Where(kv => kv.Value.Selected).ToArray())
                {
                    this._filesInProgress.Remove(kv.Key);
                }

                // Remove from the listview
                foreach (ListViewItem item in this.listViewFiles.SelectedItems)
                {
                    item.Remove();
                }
            }
        }

        private void buttonSave_Click(object sender, EventArgs e)
        {
            if (this.listViewFiles.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "Please select a file first.");
                return;
            }

            var fileData = (FileData)this.listViewFiles.SelectedItems[0].Tag;
            string[] splitPath = fileData.Name.Split(new[] {'.'});
            var extension = (string)splitPath.GetValue(splitPath.GetUpperBound(0));

            this.saveFileDialog.FileName = fileData.Name;
            this.saveFileDialog.DefaultExt = extension;
            if (this.saveFileDialog.ShowDialog(this) == DialogResult.OK)
            {
                File.WriteAllBytes(this.saveFileDialog.FileName, fileData.Bytes);
            }
        }

        private void buttonCancel_Click(object sender, EventArgs e)
        {
            if (!this.backgroundWorkerSender.IsBusy)
            {
                MessageBox.Show(this, "There is no upload currently in progress!");
                return;
            }

            this.backgroundWorkerSender.CancelAsync();
            this._bitSend.IsEnabled = false;
            this._bitSend.IsEnabled = true;
            this._bitSend_Remove(this._ownId);

            this.OnUploadStatusChange();
        }

        private static string BytesToString(long byteCount)
        {
            string[] suf = {"B", "KB", "MB", "GB", "TB", "PB", "EB"}; //Longs run out around EB
            if (byteCount == 0)
                return "0" + suf[0];
            long bytes = Math.Abs(byteCount);
            int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
            double num = Math.Round(bytes / Math.Pow(1024, place), 1);
            return (Math.Sign(byteCount) * num) + suf[place];
        }

        private void largeIconsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.listViewFiles.View = View.LargeIcon;
            this.OnViewUpdate();
        }

        private void smallIconsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.listViewFiles.View = View.SmallIcon;
            this.OnViewUpdate();
        }

        private void listToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.listViewFiles.View = View.List;
            this.OnViewUpdate();
        }

        private void tileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.listViewFiles.View = View.Tile;
            this.OnViewUpdate();
        }

        private void OnViewUpdate()
        {
            var view = this.listViewFiles.View;
            Settings.Default.View = view;
            Settings.Default.Save();

            SetCheck(this.largeIconsToolStripMenuItem, view == View.LargeIcon);
            SetCheck(this.smallIconsToolStripMenuItem, view == View.SmallIcon);
            SetCheck(this.listToolStripMenuItem, view == View.List);
            SetCheck(this.tileToolStripMenuItem, view == View.Tile);
        }

        private static void SetCheck(ToolStripMenuItem toolStripMenuItem, bool isChecked)
        {
            toolStripMenuItem.CheckState = isChecked
                ? CheckState.Checked
                : CheckState.Unchecked;
        }

        private void backgroundWorkerLogin_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            this.OnConnectivityChange();
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show(@"EEFileSend. Made by Processor.
THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.");
        }
    }
}