using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows.Forms;

namespace RuralCafe
{
    public partial class SettingsForm : Form
    {
        public SettingsForm()
        {
            InitializeComponent();
        }

        private void SettingsForm_Load(object sender, EventArgs e)
        {
            // Give all fields the current setting values. Do NOT do this in 
            // InitializeComponent, because the Designer thinks it can delete if for
            // empty default settings and produce buggy code for ComboBoxes and other stuff.
            this.textBox1.Text = Properties.Settings.Default.LOCAL_PROXY_IP_ADDRESS;
            this.textBox3.Text = Properties.Settings.Default.LOCAL_CACHE_PATH;
            this.textBox5.Text = Properties.Settings.Default.REMOTE_PROXY_IP_ADDRESS;
            this.textBox7.Text = Properties.Settings.Default.REMOTE_CACHE_PATH;
            this.textBox9.Text = Properties.Settings.Default.EXTERNAL_PROXY_IP_ADDRESS;
            this.textBox10.Text = Properties.Settings.Default.EXTERNAL_PROXY_PASS;
            this.textBox11.Text = Properties.Settings.Default.EXTERNAL_PROXY_LOGIN;
            this.textBox13.Text = Properties.Settings.Default.INDEX_PATH;
            this.textBox14.Text = Properties.Settings.Default.WIKI_DUMP_FILE;
            this.textBox15.Text = Properties.Settings.Default.WIKI_DUMP_DIR;
            this.textBox20.Text = Properties.Settings.Default.DEFAULT_SEARCH_PAGE;
            this.numericUpDown1.Value = Properties.Settings.Default.LOCAL_PROXY_LISTEN_PORT;
            this.numericUpDown2.Value = Properties.Settings.Default.EXTERNAL_PROXY_LISTEN_PORT;
            this.numericUpDown3.Value = Properties.Settings.Default.REMOTE_PROXY_LISTEN_PORT;
            this.numericUpDown4.Value = Properties.Settings.Default.LOCAL_MAXIMUM_ACTIVE_REQUESTS;
            this.numericUpDown5.Value = Properties.Settings.Default.MAXIMUM_DOWNLOAD_SPEED;
            this.numericUpDown6.Value = Properties.Settings.Default.DEFAULT_DEPTH;
            this.numericUpDown7.Value = Properties.Settings.Default.DEFAULT_QUOTA;
            this.comboBox1.DataSource = Enum.GetValues(typeof(RCProxy.NetworkStatusCode));
            this.comboBox2.DataSource = Enum.GetValues(typeof(RequestHandler.Richness));
            this.comboBox3.DataSource = Enum.GetValues(typeof(LogLevel));
            this.comboBox1.SelectedItem = Properties.Settings.Default.NETWORK_STATUS;
            this.comboBox2.SelectedItem = Properties.Settings.Default.DEFAULT_RICHNESS;
            this.comboBox3.SelectedItem = Properties.Settings.Default.LOGLEVEL;
        }

        private void button1_Click(object sender, EventArgs e)
        {
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
            // Save all settings
            Properties.Settings.Default.LOCAL_PROXY_IP_ADDRESS = this.textBox1.Text;
            Properties.Settings.Default.LOCAL_CACHE_PATH = this.textBox3.Text;
            Properties.Settings.Default.REMOTE_PROXY_IP_ADDRESS = this.textBox5.Text;
            Properties.Settings.Default.REMOTE_CACHE_PATH = this.textBox7.Text;
            Properties.Settings.Default.EXTERNAL_PROXY_IP_ADDRESS = this.textBox9.Text;
            Properties.Settings.Default.EXTERNAL_PROXY_PASS = this.textBox10.Text;
            Properties.Settings.Default.EXTERNAL_PROXY_LOGIN = this.textBox11.Text;
            Properties.Settings.Default.INDEX_PATH = this.textBox13.Text;
            Properties.Settings.Default.WIKI_DUMP_FILE = this.textBox14.Text;
            Properties.Settings.Default.WIKI_DUMP_DIR = this.textBox15.Text;
            Properties.Settings.Default.DEFAULT_SEARCH_PAGE = this.textBox20.Text;
            Properties.Settings.Default.LOCAL_PROXY_LISTEN_PORT = (int)this.numericUpDown1.Value;
            Properties.Settings.Default.EXTERNAL_PROXY_LISTEN_PORT = (int)this.numericUpDown2.Value;
            Properties.Settings.Default.REMOTE_PROXY_LISTEN_PORT = (int)this.numericUpDown3.Value;
            Properties.Settings.Default.LOCAL_MAXIMUM_ACTIVE_REQUESTS = (int)this.numericUpDown4.Value;
            Properties.Settings.Default.MAXIMUM_DOWNLOAD_SPEED = (int)this.numericUpDown5.Value;
            Properties.Settings.Default.DEFAULT_DEPTH = (int)this.numericUpDown6.Value;
            Properties.Settings.Default.DEFAULT_QUOTA = (int)this.numericUpDown7.Value;
            Properties.Settings.Default.NETWORK_STATUS = (RCProxy.NetworkStatusCode)this.comboBox1.SelectedItem;
            Properties.Settings.Default.DEFAULT_RICHNESS = (RequestHandler.Richness)this.comboBox2.SelectedItem;
            Properties.Settings.Default.LOGLEVEL = (LogLevel)this.comboBox3.SelectedItem;
            Properties.Settings.Default.Save();
            this.Close();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void label21_Click(object sender, EventArgs e)
        {

        }

        private void comboBox3_SelectedIndexChanged(object sender, EventArgs e)
        {

        }
    }
}
