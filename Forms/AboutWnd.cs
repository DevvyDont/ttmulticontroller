using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Reflection;

namespace TTMulti.Forms
{
    internal partial class AboutWnd : Form
    {
        public AboutWnd()
        {
            InitializeComponent();
            this.Icon = Properties.Resources.icon;
        }
        
        private void AboutWnd_Load(object sender, EventArgs e)
        {
            label1.Text += Application.ProductVersion;

            string prefix = "Homepage: ";
            string url = Properties.Settings.Default.homepageUrl;
            linkLabel1.Text = prefix + url;
            // Update LinkArea to cover exactly the URL portion regardless of its length
            linkLabel1.LinkArea = new System.Windows.Forms.LinkArea(prefix.Length, url.Length);

            // Ensure the form is wide enough to show the full URL without clipping
            // Leave 40px margin (20 each side)
            int requiredWidth = linkLabel1.PreferredWidth + 40;
            if (this.ClientSize.Width < requiredWidth)
                this.ClientSize = new System.Drawing.Size(requiredWidth, this.ClientSize.Height);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start(linkLabel1.Text.Substring(e.Link.Start, e.Link.Length));
        }
    }
}
