using System;
using System.Drawing;
using System.Windows.Forms;
using GHelper.Fan;

namespace GHelper
{
    public partial class Fans
    {
        // UI elemanları
        private CheckBox chkCrossFan;
        private NumericUpDown numCrossTemp;
        private NumericUpDown numCrossHyst;
        private Label lblCrossTemp;
        private Label lblCrossHyst;
        private ToolTip ttCross;

        // Initialize UI controls (çağırmak için: Fans() constructor'ında InitSmartCrossFanUI(); )
        private void InitSmartCrossFanUI()
        {
            // Guard: zaten eklenmişse tekrar ekleme
            if (chkCrossFan != null) return;

            // ToolTip
            ttCross = new ToolTip();

            // Checkbox
            chkCrossFan = new CheckBox();
            chkCrossFan.AutoSize = true;
            chkCrossFan.Text = "Smart Cross-Fan";
            chkCrossFan.Checked = AppConfig.Is("cross_fan");
            chkCrossFan.CheckedChanged += ChkCrossFan_CheckedChanged;
            ttCross.SetToolTip(chkCrossFan, "Enable Smart Cross-Fan override when one component is hot.");

            // Threshold numeric
            lblCrossTemp = new Label();
            lblCrossTemp.AutoSize = true;
            lblCrossTemp.Text = "Threshold (°C):";
            lblCrossTemp.TextAlign = ContentAlignment.MiddleLeft;

            numCrossTemp = new NumericUpDown();
            numCrossTemp.Minimum = 40;
            numCrossTemp.Maximum = 110;
            numCrossTemp.Value = AppConfig.Get("cross_temp", 75);
            numCrossTemp.Width = 50;
            numCrossTemp.ValueChanged += NumCross_ValueChanged;
            ttCross.SetToolTip(numCrossTemp, "Temperature above which cross-fan helper engages.");

            // Hysteresis numeric
            lblCrossHyst = new Label();
            lblCrossHyst.AutoSize = true;
            lblCrossHyst.Text = "Hysteresis (°C):";
            lblCrossHyst.TextAlign = ContentAlignment.MiddleLeft;

            numCrossHyst = new NumericUpDown();
            numCrossHyst.Minimum = 0;
            numCrossHyst.Maximum = 20;
            numCrossHyst.Value = AppConfig.Get("cross_hyst", 5);
            numCrossHyst.Width = 40;
            numCrossHyst.ValueChanged += NumCross_ValueChanged;
            ttCross.SetToolTip(numCrossHyst, "Hysteresis to avoid hunting.");

            // Container panel (top-right anchored)
            var pnl = new Panel();
            pnl.AutoSize = true;
            pnl.BackColor = Color.Transparent;

            // Layout: horizontal stack
            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.AutoSize = true;
            flow.FlowDirection = FlowDirection.LeftToRight;
            flow.WrapContents = false;
            flow.Margin = new Padding(6);
            flow.Controls.Add(chkCrossFan);
            flow.Controls.Add(lblCrossTemp);
            flow.Controls.Add(numCrossTemp);
            flow.Controls.Add(lblCrossHyst);
            flow.Controls.Add(numCrossHyst);

            pnl.Controls.Add(flow);

            // Position: top-right corner inside the form with margin
            pnl.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            pnl.Location = new Point(this.ClientSize.Width - 420, 8); // tolerant default
            pnl.Margin = new Padding(0);

            // Adjust when form resized to keep top-right placement
            this.Resize += (s, e) =>
            {
                pnl.Location = new Point(Math.Max(8, this.ClientSize.Width - pnl.PreferredSize.Width - 8), 8);
            };

            // Add to form controls
            this.Controls.Add(pnl);

            // Initial apply if enabled
            if (chkCrossFan.Checked)
            {
                AppConfig.Set("cross_fan", 1);
                AppConfig.Set("cross_temp", (int)numCrossTemp.Value);
                AppConfig.Set("cross_hyst", (int)numCrossHyst.Value);
                try { SmartCrossFan.Apply(true); } catch { }
            }
            else
            {
                AppConfig.Set("cross_fan", 0);
            }
        }

        private void ChkCrossFan_CheckedChanged(object? sender, EventArgs e)
        {
            AppConfig.Set("cross_fan", chkCrossFan.Checked ? 1 : 0);

            // Apply immediately (force)
            try
            {
                SmartCrossFan.Apply(true);
            }
            catch (Exception ex)
            {
                Logger.WriteLine("SmartCrossFan UI apply error: " + ex.ToString());
            }
        }

        private void NumCross_ValueChanged(object? sender, EventArgs e)
        {
            AppConfig.Set("cross_temp", (int)numCrossTemp.Value);
            AppConfig.Set("cross_hyst", (int)numCrossHyst.Value);

            // Re-evaluate immediately if enabled
            if (AppConfig.Is("cross_fan"))
            {
                try
                {
                    SmartCrossFan.Apply(true);
                }
                catch (Exception ex)
                {
                    Logger.WriteLine("SmartCrossFan UI apply error: " + ex.ToString());
                }
            }
        }
    }
}