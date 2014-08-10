using System;
using System.Windows.Forms;
using EEFileDrop.Properties;

namespace EEFileDrop
{
    public partial class ConnectForm : Form
    {
        public ConnectForm()
        {
            this.InitializeComponent();

            this.textBoxEmail.Text = Settings.Default.Email;
            this.textBoxPassword.Text = Settings.Default.Password;
            this.textBoxWorldId.Text = Settings.Default.WorldId;
        }

        private void buttonCancel_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
        }

        private void buttonConnect_Click(object sender, EventArgs e)
        {
            Settings.Default.Email = this.textBoxEmail.Text;
            Settings.Default.Password = this.textBoxPassword.Text;
            Settings.Default.WorldId = this.textBoxWorldId.Text;
            Settings.Default.Save();

            this.DialogResult = DialogResult.OK;
        }
    }
}