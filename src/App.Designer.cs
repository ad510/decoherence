﻿namespace Decoherence
{
    partial class App
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.SuspendLayout();
            // 
            // App
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.Black;
            this.ClientSize = new System.Drawing.Size(284, 262);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.Name = "App";
            this.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            this.Text = "possibly decoherence";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.App_FormClosing);
            this.Load += new System.EventHandler(this.App_Load);
            this.KeyDown += new System.Windows.Forms.KeyEventHandler(this.App_KeyDown);
            this.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(this.App_MouseDoubleClick);
            this.MouseDown += new System.Windows.Forms.MouseEventHandler(this.App_MouseDown);
            this.MouseMove += new System.Windows.Forms.MouseEventHandler(this.App_MouseMove);
            this.MouseUp += new System.Windows.Forms.MouseEventHandler(this.App_MouseUp);
            this.ResumeLayout(false);

        }

        #endregion
    }
}
