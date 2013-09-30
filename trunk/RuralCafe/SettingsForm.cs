using RuralCafe.Crawler;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace RuralCafe
{
    /// <summary>
    /// The settings form shown on start up.
    /// </summary>
    public partial class SettingsForm : Form
    {
        /// <summary>
        /// A new Settings form
        /// </summary>
        public SettingsForm()
        {
            InitializeComponent();
        }

        /// <summary>
        /// On loading the form.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SettingsForm_Load(object sender, EventArgs e)
        {
            // Give all fields the current setting values. Do NOT do this in 
            // InitializeComponent, because the Designer thinks it can delete it for
            // empty default settings and produce buggy code for ComboBoxes and other stuff.
            this.textBox1.Text = Properties.Connection.Default.LOCAL_PROXY_IP_ADDRESS;
            this.textBox3.Text = Properties.Files.Default.LOCAL_CACHE_PATH;
            this.textBox5.Text = Properties.Connection.Default.REMOTE_PROXY_IP_ADDRESS;
            this.textBox7.Text = Properties.Files.Default.REMOTE_CACHE_PATH;
            this.textBox9.Text = Properties.Connection.Default.EXTERNAL_PROXY_IP_ADDRESS;
            this.textBox10.Text = Properties.Connection.Default.EXTERNAL_PROXY_PASS;
            this.textBox11.Text = Properties.Connection.Default.EXTERNAL_PROXY_LOGIN;
            this.textBox13.Text = Properties.Files.Default.LOCAL_INDEX_PATH;
            this.textBox14.Text = Properties.Files.Default.LOCAL_WIKI_DUMP_FILE;
            this.textBox2.Text = Properties.Files.Default.BASE_DIR;
            this.textBox20.Text = Properties.Files.Default.DEFAULT_SEARCH_PAGE;
            this.numericUpDown1.Value = Properties.Connection.Default.LOCAL_PROXY_LISTEN_PORT;
            this.numericUpDown2.Value = Properties.Connection.Default.EXTERNAL_PROXY_LISTEN_PORT;
            this.numericUpDown3.Value = Properties.Connection.Default.REMOTE_PROXY_LISTEN_PORT;
            this.numericUpDown5.Value = Properties.Network.Default.MAXIMUM_DOWNLOAD_SPEED;
            this.numericUpDown6.Value = Properties.Settings.Default.DEFAULT_DEPTH;
            this.numericUpDown7.Value = Properties.Settings.Default.DEFAULT_QUOTA;
            this.numericUpDown8.Value = Properties.Settings.Default.DNS_CACHE_TTL;
            this.numericUpDown9.Value = Properties.Files.Default.LOCAL_MAX_CACHE_SIZE_MIB;
            this.numericUpDown10.Value = Properties.Files.Default.REMOTE_MAX_CACHE_SIZE_MIB;
            this.comboBox1.DataSource = Enum.GetValues(typeof(RCLocalProxy.NetworkStatusCode));
            this.comboBox2.DataSource = Enum.GetValues(typeof(RequestHandler.Richness));
            this.comboBox3.DataSource = Enum.GetValues(typeof(LogLevel));
            this.checkBox1.Checked = Properties.Network.Default.DETECT_NETWORK_AUTO;
            checkBox1_CheckedChanged(null, null);
            this.checkBox2.Checked = Properties.Settings.Default.FORCE_LOGIN;
            this.checkBox3.Checked = Properties.Settings.Default.SHOW_SURVEY;
            this.comboBox1.SelectedItem = Properties.Network.Default.NETWORK_STATUS;
            this.comboBox2.SelectedItem = Properties.Settings.Default.DEFAULT_RICHNESS;
            this.comboBox3.SelectedItem = Properties.Settings.Default.LOGLEVEL;
        }

        /// <summary>
        /// The Save button.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void button1_Click(object sender, EventArgs e)
        {
            // Check if settings are valid.
            // Dummy for out parameter
            IPAddress ipAddressDummy;
            if (!this.textBox1.Text.Equals("") && !IPAddress.TryParse(this.textBox1.Text, out ipAddressDummy))
            {
                MessageBox.Show(this, this.textBox1.Text + " is not a valid IP address.", "Invalid IP address",
                    MessageBoxButtons.OK);
                return;
            }
            if (!this.textBox5.Text.Equals("") && !IPAddress.TryParse(this.textBox5.Text, out ipAddressDummy))
            {
                MessageBox.Show(this, this.textBox5.Text + " is not a valid IP address.", "Invalid IP address",
                    MessageBoxButtons.OK);
                return;
            }
            if (!this.textBox9.Text.Equals("") && !IPAddress.TryParse(this.textBox9.Text, out ipAddressDummy))
            {
                MessageBox.Show(this, this.textBox9.Text + " is not a valid IP address.", "Invalid IP address",
                    MessageBoxButtons.OK);
                return;
            }
            // Remove all trailing slashes from paths
            foreach (TextBox textBox in new TextBox[] { this.textBox2, this.textBox3, this.textBox7, this.textBox13 })
            {
                if (textBox.Text.EndsWith("" + Path.DirectorySeparatorChar))
                {
                    textBox.Text = textBox.Text.Substring(0, textBox.Text.Length - 1);
                }
            }

            // Set all settings
            Properties.Connection.Default.LOCAL_PROXY_IP_ADDRESS = this.textBox1.Text;
            Properties.Files.Default.LOCAL_CACHE_PATH = this.textBox3.Text;
            Properties.Connection.Default.REMOTE_PROXY_IP_ADDRESS = this.textBox5.Text;
            Properties.Files.Default.REMOTE_CACHE_PATH = this.textBox7.Text;
            Properties.Connection.Default.EXTERNAL_PROXY_IP_ADDRESS = this.textBox9.Text;
            Properties.Connection.Default.EXTERNAL_PROXY_PASS = this.textBox10.Text;
            Properties.Connection.Default.EXTERNAL_PROXY_LOGIN = this.textBox11.Text;
            Properties.Files.Default.LOCAL_INDEX_PATH = this.textBox13.Text;
            Properties.Files.Default.LOCAL_WIKI_DUMP_FILE = this.textBox14.Text;
            Properties.Files.Default.BASE_DIR = this.textBox2.Text;
            Properties.Files.Default.DEFAULT_SEARCH_PAGE = this.textBox20.Text;
            Properties.Connection.Default.LOCAL_PROXY_LISTEN_PORT = (int)this.numericUpDown1.Value;
            Properties.Connection.Default.EXTERNAL_PROXY_LISTEN_PORT = (int)this.numericUpDown2.Value;
            Properties.Connection.Default.REMOTE_PROXY_LISTEN_PORT = (int)this.numericUpDown3.Value;
            Properties.Network.Default.MAXIMUM_DOWNLOAD_SPEED = (int)this.numericUpDown5.Value;
            Properties.Settings.Default.DEFAULT_DEPTH = (int)this.numericUpDown6.Value;
            Properties.Settings.Default.DEFAULT_QUOTA = (int)this.numericUpDown7.Value;
            Properties.Settings.Default.DNS_CACHE_TTL = (int)this.numericUpDown8.Value;
            Properties.Files.Default.LOCAL_MAX_CACHE_SIZE_MIB = (int)this.numericUpDown9.Value;
            Properties.Files.Default.REMOTE_MAX_CACHE_SIZE_MIB = (int)this.numericUpDown10.Value;
            Properties.Network.Default.DETECT_NETWORK_AUTO = this.checkBox1.Checked;
            Properties.Settings.Default.FORCE_LOGIN = this.checkBox2.Checked;
            Properties.Settings.Default.SHOW_SURVEY = this.checkBox3.Checked;
            Properties.Network.Default.NETWORK_STATUS = (RCLocalProxy.NetworkStatusCode)this.comboBox1.SelectedItem;
            Properties.Settings.Default.DEFAULT_RICHNESS = (RequestHandler.Richness)this.comboBox2.SelectedItem;
            Properties.Settings.Default.LOGLEVEL = (LogLevel)this.comboBox3.SelectedItem;

            // Save all settings
            Properties.Settings.Default.Save();
            Properties.Connection.Default.Save();
            Properties.Files.Default.Save();
            Properties.Network.Default.Save();

            DialogResult = DialogResult.OK;
            this.Close();
        }

        /// <summary>
        /// The cancel button.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void button2_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            this.Close();
        }

        /// <summary>
        /// When detect network auto is (un)checked.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            // Enable or disable the network status combo box.
            this.comboBox1.Enabled = !this.checkBox1.Checked;
        }

        /// <summary>
        /// Opens the LP blacklist in the default editor.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void button3_Click(object sender, EventArgs e)
        {
            // Edit the LP  blacklist
            string blacklistFileName = this.textBox2.Text + Path.DirectorySeparatorChar // textBox2: the base dir
                + Program.LOCAL_PROXY_PATH + "blacklist.txt";
            OpenBlacklistFile(blacklistFileName);
        }

        /// <summary>
        /// Opens the RP blacklist in the default editor.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void button4_Click(object sender, EventArgs e)
        {
            // Edit the RP blacklist
            string blacklistFileName = this.textBox2.Text + Path.DirectorySeparatorChar // textBox2: the base dir
                + Program.REMOTE_PROXY_PATH + "blacklist.txt";
            OpenBlacklistFile(blacklistFileName);
        }

        /// <summary>
        /// Opens a blacklist file. Copies the default blacklist to that place, if there is none.
        /// </summary>
        /// <param name="blacklistFileName">The full path to the blacklist.</param>
        private void OpenBlacklistFile(string blacklistFileName)
        {
            // Copy the default file to the proxy folder, if there is no blacklist
            if (!File.Exists(blacklistFileName))
            {
                try
                {
                    File.Copy("blacklist.txt", blacklistFileName);
                }
                catch (Exception exp)
                {
                    MessageBox.Show(this, "Could not copy the default blacklist to the proxy folder: " + exp.Message,
                        "Blacklist error", MessageBoxButtons.OK);
                    return;
                }
            }
            // View the blacklist in the default editor.
            try
            {
                Process.Start(blacklistFileName);
            }
            catch (Exception exp)
            {
                MessageBox.Show(this, "Could not open the blacklist file: " + exp.Message,
                        "Blacklist error", MessageBoxButtons.OK);
            }
        }

        /// <summary>
        /// The Save and start crawler button.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void button5_Click(object sender, EventArgs e)
        {
            // Save
            this.button1_Click(null, null);
            // Override the DialogResult with Yes, which is used for "Yes, start the damn crawler."
            DialogResult = DialogResult.Yes;
        }
    }
}
