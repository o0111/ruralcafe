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
            this.localProxyIPTextBox.Text = Properties.Connection.Default.LOCAL_PROXY_IP_ADDRESS;
            this.localCachePathTextBox.Text = Properties.Files.Default.LOCAL_CACHE_PATH;
            this.remoteProxyIPTextBox.Text = Properties.Connection.Default.REMOTE_PROXY_IP_ADDRESS;
            this.remoteCachePathTextBox.Text = Properties.Files.Default.REMOTE_CACHE_PATH;
            this.gatewayProxyIPTextBox.Text = Properties.Connection.Default.EXTERNAL_PROXY_IP_ADDRESS;
            this.gatewayProxyPassTextBox.Text = Properties.Connection.Default.EXTERNAL_PROXY_PASS;
            this.gatewayProxyUserTextBox.Text = Properties.Connection.Default.EXTERNAL_PROXY_LOGIN;
            this.indexPathTextBox.Text = Properties.Files.Default.LOCAL_INDEX_PATH;
            this.wikiDumpFileTextBox.Text = Properties.Files.Default.LOCAL_WIKI_DUMP_FILE;
            this.baseDirectoryTextBox.Text = Properties.Files.Default.BASE_DIR;
            this.searchPageTextBox.Text = Properties.Files.Default.DEFAULT_SEARCH_PAGE;
            this.localPortNUD.Value = Properties.Connection.Default.LOCAL_PROXY_LISTEN_PORT;
            this.gatewayProxyPortNUD.Value = Properties.Connection.Default.EXTERNAL_PROXY_LISTEN_PORT;
            this.remotePortNUD.Value = Properties.Connection.Default.REMOTE_PROXY_LISTEN_PORT;
            this.maxDownSpeedNUD.Value = Properties.Network.Default.MAXIMUM_DOWNLOAD_SPEED;
            this.depthNUD.Value = Properties.Settings.Default.DEFAULT_DEPTH;
            this.qoutaNUD.Value = Properties.Settings.Default.DEFAULT_QUOTA;
            this.dnsCacheTTLNUD.Value = Properties.Settings.Default.DNS_CACHE_TTL;
            this.localMaxCacheSizeNUD.Value = Properties.Files.Default.LOCAL_MAX_CACHE_SIZE_MIB;
            this.remoteMaxCacheSizeNUD.Value = Properties.Files.Default.REMOTE_MAX_CACHE_SIZE_MIB;
            this.networkStatusComboBox.DataSource = Enum.GetValues(typeof(RCLocalProxy.NetworkStatusCode));
            this.richnessComboBox.DataSource = Enum.GetValues(typeof(RequestHandler.Richness));
            this.logLevelComboBox.DataSource = Enum.GetValues(typeof(LogLevel));
            this.detectNetworkStatusCheckBox.Checked = Properties.Network.Default.DETECT_NETWORK_AUTO;
            detectNetworkStatusCheckBox_CheckedChanged(null, null);
            this.forceLoginCheckBox.Checked = Properties.Settings.Default.FORCE_LOGIN;
            this.showSurveyCheckBox.Checked = Properties.Settings.Default.SHOW_SURVEY;
            this.networkStatusComboBox.SelectedItem = Properties.Network.Default.NETWORK_STATUS;
            this.richnessComboBox.SelectedItem = Properties.Settings.Default.DEFAULT_RICHNESS;
            this.logLevelComboBox.SelectedItem = Properties.Settings.Default.LOGLEVEL;

            localProxyIPTextBox_TextChanged(null, null);
        }

        private void detectNetworkStatusCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            // Enable or disable the network status combo box.
            this.networkStatusComboBox.Enabled = !this.detectNetworkStatusCheckBox.Checked;
        }

        private void saveButton_Click(object sender, EventArgs e)
        {
            // Check if settings are valid.
            // Dummy for out parameter
            IPAddress ipAddressDummy;
            if (!this.localProxyIPTextBox.Text.Equals("") && !IPAddress.TryParse(this.localProxyIPTextBox.Text, out ipAddressDummy))
            {
                MessageBox.Show(this, this.localProxyIPTextBox.Text + " is not a valid IP address.", "Invalid IP address",
                    MessageBoxButtons.OK);
                return;
            }
            if (!this.remoteProxyIPTextBox.Text.Equals("") && !IPAddress.TryParse(this.remoteProxyIPTextBox.Text, out ipAddressDummy))
            {
                MessageBox.Show(this, this.remoteProxyIPTextBox.Text + " is not a valid IP address.", "Invalid IP address",
                    MessageBoxButtons.OK);
                return;
            }
            if (!this.gatewayProxyIPTextBox.Text.Equals("") && !IPAddress.TryParse(this.gatewayProxyIPTextBox.Text, out ipAddressDummy))
            {
                MessageBox.Show(this, this.gatewayProxyIPTextBox.Text + " is not a valid IP address.", "Invalid IP address",
                    MessageBoxButtons.OK);
                return;
            }
            // Remove all trailing slashes from paths
            foreach (TextBox textBox in new TextBox[] { this.baseDirectoryTextBox, this.localCachePathTextBox, this.remoteCachePathTextBox, this.indexPathTextBox })
            {
                if (textBox.Text.EndsWith("" + Path.DirectorySeparatorChar))
                {
                    textBox.Text = textBox.Text.Substring(0, textBox.Text.Length - 1);
                }
            }

            // Set all settings
            Properties.Connection.Default.LOCAL_PROXY_IP_ADDRESS = this.localProxyIPTextBox.Text;
            Properties.Files.Default.LOCAL_CACHE_PATH = this.localCachePathTextBox.Text;
            Properties.Connection.Default.REMOTE_PROXY_IP_ADDRESS = this.remoteProxyIPTextBox.Text;
            Properties.Files.Default.REMOTE_CACHE_PATH = this.remoteCachePathTextBox.Text;
            Properties.Connection.Default.EXTERNAL_PROXY_IP_ADDRESS = this.gatewayProxyIPTextBox.Text;
            Properties.Connection.Default.EXTERNAL_PROXY_PASS = this.gatewayProxyPassTextBox.Text;
            Properties.Connection.Default.EXTERNAL_PROXY_LOGIN = this.gatewayProxyUserTextBox.Text;
            Properties.Files.Default.LOCAL_INDEX_PATH = this.indexPathTextBox.Text;
            Properties.Files.Default.LOCAL_WIKI_DUMP_FILE = this.wikiDumpFileTextBox.Text;
            Properties.Files.Default.BASE_DIR = this.baseDirectoryTextBox.Text;
            Properties.Files.Default.DEFAULT_SEARCH_PAGE = this.searchPageTextBox.Text;
            Properties.Connection.Default.LOCAL_PROXY_LISTEN_PORT = (int)this.localPortNUD.Value;
            Properties.Connection.Default.EXTERNAL_PROXY_LISTEN_PORT = (int)this.gatewayProxyPortNUD.Value;
            Properties.Connection.Default.REMOTE_PROXY_LISTEN_PORT = (int)this.remotePortNUD.Value;
            Properties.Network.Default.MAXIMUM_DOWNLOAD_SPEED = (int)this.maxDownSpeedNUD.Value;
            Properties.Settings.Default.DEFAULT_DEPTH = (int)this.depthNUD.Value;
            Properties.Settings.Default.DEFAULT_QUOTA = (int)this.qoutaNUD.Value;
            Properties.Settings.Default.DNS_CACHE_TTL = (int)this.dnsCacheTTLNUD.Value;
            Properties.Files.Default.LOCAL_MAX_CACHE_SIZE_MIB = (int)this.localMaxCacheSizeNUD.Value;
            Properties.Files.Default.REMOTE_MAX_CACHE_SIZE_MIB = (int)this.remoteMaxCacheSizeNUD.Value;
            Properties.Network.Default.DETECT_NETWORK_AUTO = this.detectNetworkStatusCheckBox.Checked;
            Properties.Settings.Default.FORCE_LOGIN = this.forceLoginCheckBox.Checked;
            Properties.Settings.Default.SHOW_SURVEY = this.showSurveyCheckBox.Checked;
            Properties.Network.Default.NETWORK_STATUS = (RCLocalProxy.NetworkStatusCode)this.networkStatusComboBox.SelectedItem;
            Properties.Settings.Default.DEFAULT_RICHNESS = (RequestHandler.Richness)this.richnessComboBox.SelectedItem;
            Properties.Settings.Default.LOGLEVEL = (LogLevel)this.logLevelComboBox.SelectedItem;

            // Save all settings
            Properties.Settings.Default.Save();
            Properties.Connection.Default.Save();
            Properties.Files.Default.Save();
            Properties.Network.Default.Save();

            DialogResult = DialogResult.OK;
            this.Close();
        }

        private void saveAndCrawlButton_Click(object sender, EventArgs e)
        {
            // Save
            this.saveButton_Click(null, null);
            // Override the DialogResult with Yes, which is used for "Yes, start the damn crawler."
            DialogResult = DialogResult.Yes;
        }

        private void cancelButton_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            this.Close();
        }

        private void localEditBlacklistButton_Click(object sender, EventArgs e)
        {
            // Edit the LP  blacklist
            string blacklistFileName = this.baseDirectoryTextBox.Text + Path.DirectorySeparatorChar // textBox2: the base dir
                + Program.LOCAL_PROXY_PATH + "blacklist.txt";
            OpenBlacklistFile(blacklistFileName);
        }

        private void remoteEditBlacklistButton_Click(object sender, EventArgs e)
        {
            // Edit the RP blacklist
            string blacklistFileName = this.baseDirectoryTextBox.Text + Path.DirectorySeparatorChar // textBox2: the base dir
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

        private void localProxyIPTextBox_TextChanged(object sender, EventArgs e)
        {
            // Enable or disable the "Save and Start Crawler" Button
            this.saveAndCrawlButton.Enabled = !String.IsNullOrEmpty(this.localProxyIPTextBox.Text);
        }
    }
}
