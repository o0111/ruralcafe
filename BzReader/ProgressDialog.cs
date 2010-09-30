using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace BzReader
{
    public partial class ProgressDialog : Form
    {
        /// <summary>
        /// Handles the ProgressChanged event from indexers
        /// </summary>
        /// <param name="sender">Indexer</param>
        /// <param name="e">Progress event</param>
        private delegate void ProgressChangedDelegate(object sender, ProgressChangedEventArgs e);
        /// <summary>
        /// The indexer we're associated with
        /// </summary>
        private Indexer ixr;
        /// <summary>
        /// Whether indexing is currently being executed
        /// </summary>
        private bool indexingRunning;

        public ProgressDialog(Indexer indexer)
        {
            InitializeComponent();

            ixr = indexer;

            ixr.ProgressChanged += new ProgressChangedEventHandler(ixr_ProgressChanged);
        }

        private void ProgressDialog_Shown(object sender, EventArgs e)
        {
            ixr.CreateIndex();

            indexingRunning = true;
        }

        private void btnDone_Click(object sender, EventArgs e)
        {
            if (indexingRunning)
            {
                btnDone.Enabled = false;

                textBox.AppendText("Aborting" + Environment.NewLine);

                ixr.AbortIndex();
            }
            else
            {
                // Due to failure

                DialogResult = DialogResult.Abort;
                Close();
            }
        }

        private void Indexer_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            IndexingProgress ip = (IndexingProgress)e.UserState;
            
            if (!String.IsNullOrEmpty(ip.Message))
            {
                textBox.AppendText(ip.Message + Environment.NewLine);
            }

            if (e.ProgressPercentage > 0)
            {
                progressBar.Value = e.ProgressPercentage;
            }

            if (ip.IndexingState == IndexingProgress.State.Failure)
            {
                btnDone.Text = "Close";
            }

            if (ip.IndexingState == IndexingProgress.State.Finished)
            {
                indexingRunning = false;
                
                if (!btnDone.Enabled)
                {
                    // Due to abort

                    DialogResult = DialogResult.Abort;
                    Close();
                }
                else if (btnDone.Text.Equals("Cancel", StringComparison.InvariantCultureIgnoreCase))
                {
                    // Due to proper finish

                    DialogResult = DialogResult.OK;
                    Close();
                }
            }
        }

        #region Helper methods

        private void ixr_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            Invoke(new ProgressChangedDelegate(Indexer_ProgressChanged), sender, e);
        }

        #endregion
    }
}