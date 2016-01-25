﻿/* -LICENSE-START-
** Copyright (c) 2011 Blackmagic Design
**
** Permission is hereby granted, free of charge, to any person or organization
** obtaining a copy of the software and accompanying documentation covered by
** this license (the "Software") to use, reproduce, display, distribute,
** execute, and transmit the Software, and to prepare derivative works of the
** Software, and to permit third-parties to whom the Software is furnished to
** do so, all subject to the following:
** 
** The copyright notices in the Software and this entire statement, including
** the above license grant, this restriction and the following disclaimer,
** must be included in all copies of the Software, in whole or in part, and
** all derivative works of the Software, unless such copies or derivative
** works are solely in the form of machine-executable object code generated by
** a source language processor.
** 
** THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
** IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
** FITNESS FOR A PARTICULAR PURPOSE, TITLE AND NON-INFRINGEMENT. IN NO EVENT
** SHALL THE COPYRIGHT HOLDERS OR ANYONE DISTRIBUTING THE SOFTWARE BE LIABLE
** FOR ANY DAMAGES OR OTHER LIABILITY, WHETHER IN CONTRACT, TORT OR OTHERWISE,
** ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
** DEALINGS IN THE SOFTWARE.
** -LICENSE-END-
*/

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

using System.Runtime.InteropServices;

using BMDSwitcherAPI;

namespace SwitcherPanelCSharp
{
    public partial class TallyControl : Form
    {
        private IBMDSwitcherDiscovery m_switcherDiscovery;
        private IBMDSwitcher m_switcher;
        private IBMDSwitcherMixEffectBlock m_mixEffectBlock1;

        private SwitcherMonitor m_switcherMonitor;
        private MixEffectBlockMonitor m_mixEffectBlockMonitor;

        private List<InputMonitor> m_inputMonitors = new List<InputMonitor>();

        private const int TALLY_CHANNEL_COUNT = 6;

        public TallyControl()
        {
            InitializeComponent();

            m_switcherMonitor = new SwitcherMonitor();
            // note: this invoke pattern ensures our callback is called in the main thread. We are making double
            // use of lambda expressions here to achieve this.
            // Essentially, the events will arrive at the callback class (implemented by our monitor classes)
            // on a separate thread. We must marshell these to the main thread, and we're doing this by calling
            // invoke on the Windows Forms object. The lambda expression is just a simplification.
            m_switcherMonitor.SwitcherDisconnected += new SwitcherEventHandler((s, a) => this.Invoke((Action)(() => SwitcherDisconnected())));

            m_mixEffectBlockMonitor = new MixEffectBlockMonitor();
            m_mixEffectBlockMonitor.ProgramInputChanged += new SwitcherEventHandler((s, a) => this.Invoke((Action)(() => UpdateProgramButtonSelection())));
            m_mixEffectBlockMonitor.TransitionPositionChanged += new SwitcherEventHandler((s, a) => this.Invoke((Action)(() => UpdateProgramButtonSelection())));

            m_switcherDiscovery = new CBMDSwitcherDiscovery();
            if (m_switcherDiscovery == null)
            {
                MessageBox.Show("Could not create Switcher Discovery Instance.\nATEM Switcher Software may not be installed.", "Error");
                Environment.Exit(1);
            }
            
            SwitcherDisconnected();		// start with switcher disconnected

            // Grab a list of serial ports
            cmbPort.Items.Clear();
            foreach (string s in System.IO.Ports.SerialPort.GetPortNames())
            {
                cmbPort.Items.Add(s);
            }

            ForceControlOrderingInCollection();

            // Configure all the drop-downs for channel assigns
            for (int i = 0; i < pnlChannelDropDowns.Controls.Count; i++)
            {
                ComboBox thiscmb = (ComboBox)pnlChannelDropDowns.Controls[i];

                thiscmb.Items.Clear();
                thiscmb.Items.Add("");
                for (int j = 1; j <= TALLY_CHANNEL_COUNT; j++)
                {
                    thiscmb.Items.Add(j.ToString());
                }

                if (i < TALLY_CHANNEL_COUNT)
                {
                    // i is zero-indexed but tally channels are one-indexed (zero is the blank)
                    thiscmb.SelectedIndex = i + 1;
                }
                else
                {
                    thiscmb.SelectedIndex = 0;
                }

                thiscmb.SelectedIndexChanged += new EventHandler(cmbTally_SelectedIndexChange);
            }

        }

        /// <summary>
        /// Force controls into the correct ordering in their parent collections
        /// THIS FUNCTION MUST BE UPDATED IF THE CONTROLS CHANGE OR ORDERING WILL BE WRONG (maybe)
        /// </summary> 
        private void ForceControlOrderingInCollection()
        {
            pnlChannelDropDowns.Controls.SetChildIndex(cmbTallyChannel1, 0);
            pnlChannelDropDowns.Controls.SetChildIndex(cmbTallyChannel2, 1);
            pnlChannelDropDowns.Controls.SetChildIndex(cmbTallyChannel3, 2);
            pnlChannelDropDowns.Controls.SetChildIndex(cmbTallyChannel4, 3);
            pnlChannelDropDowns.Controls.SetChildIndex(cmbTallyChannel5, 4);
            pnlChannelDropDowns.Controls.SetChildIndex(cmbTallyChannel6, 5);
            pnlChannelDropDowns.Controls.SetChildIndex(cmbTallyChannel7, 6);
            pnlChannelDropDowns.Controls.SetChildIndex(cmbTallyChannel8, 7);

            pnlLampLabels.Controls.SetChildIndex(lblLamp1, 0);
            pnlLampLabels.Controls.SetChildIndex(lblLamp2, 1);
            pnlLampLabels.Controls.SetChildIndex(lblLamp3, 2);
            pnlLampLabels.Controls.SetChildIndex(lblLamp4, 3);
            pnlLampLabels.Controls.SetChildIndex(lblLamp5, 4);
            pnlLampLabels.Controls.SetChildIndex(lblLamp6, 5);
            pnlLampLabels.Controls.SetChildIndex(lblLamp7, 6);
            pnlLampLabels.Controls.SetChildIndex(lblLamp8, 7);
        }

        private void SwitcherConnected()
        {
            buttonConnect.Enabled = false;

            // Get the switcher name:
            string switcherName;
            m_switcher.GetProductName(out switcherName);
            textBoxSwitcherName.Text = switcherName;

            // Install SwitcherMonitor callbacks:
            m_switcher.AddCallback(m_switcherMonitor);

            // We want to get the first Mix Effect block (ME 1). We create a ME iterator,
            // and then get the first one:
            m_mixEffectBlock1 = null;
            IBMDSwitcherMixEffectBlockIterator meIterator;
            SwitcherAPIHelper.CreateIterator(m_switcher, out meIterator);

            if (meIterator != null)
            {
                meIterator.Next(out m_mixEffectBlock1);
            }

            if (m_mixEffectBlock1 == null)
            {
                reportMessage("Unexpected: Could not get first mix effect block", true);
                return;
            }

            // Install MixEffectBlockMonitor callbacks:
            m_mixEffectBlock1.AddCallback(m_mixEffectBlockMonitor);
            UpdateProgramButtonSelection();

            reportMessage("Connection succeeded!");
        }

        private void SwitcherDisconnected()
        {
            buttonConnect.Enabled = true;
            textBoxSwitcherName.Text = "";

            if (m_mixEffectBlock1 != null)
            {
                // Remove callback
                m_mixEffectBlock1.RemoveCallback(m_mixEffectBlockMonitor);

                // Release reference
                m_mixEffectBlock1 = null;
            }

            if (m_switcher != null)
            {
                // Remove callback:
                m_switcher.RemoveCallback(m_switcherMonitor);

                // release reference:
                m_switcher = null;

                // This probably wasn't good...
                reportMessage("Switcher Disconnected!!!", true);
            }

        }

        private void UpdateProgramButtonSelection()
        {
            long programId;
            int previewLive;
            long previewId;

            try
            {
                m_mixEffectBlock1.GetInt(_BMDSwitcherMixEffectBlockPropertyId.bmdSwitcherMixEffectBlockPropertyIdProgramInput, out programId);
                m_mixEffectBlock1.GetFlag(_BMDSwitcherMixEffectBlockPropertyId.bmdSwitcherMixEffectBlockPropertyIdPreviewLive, out previewLive);
                if (previewLive > 0)
                {
                    m_mixEffectBlock1.GetInt(_BMDSwitcherMixEffectBlockPropertyId.bmdSwitcherMixEffectBlockPropertyIdPreviewInput, out previewId);
                }
                else
                {
                    previewId = -1;
                }
            }
            catch (Exception)
            {
                return;
            }

            // Clear all the backgrounds
            foreach (Label thislbl in pnlLampLabels.Controls) 
            {
                thislbl.BackColor = Color.DarkGray;
            }

            // Work out whether to drive preview
            int prev_tally_line = -1;
            if (previewId > 0 && previewId <= pnlChannelDropDowns.Controls.Count)
            {
                // Channels in the mixer are one indexed, combo boxes in the controlset are zero indexed
                int combo_index = (int)previewId - 1;

                // Work out which electrical channel matches this mixer channel (electrical channels zero-indexed)
                // This also helpfully ignores the zero value (blank)
                prev_tally_line = ((ComboBox)pnlChannelDropDowns.Controls[combo_index]).SelectedIndex - 1;

                // And update that label to be green (preview colour)
                ((Label)pnlLampLabels.Controls[combo_index]).BackColor = Color.Red;
            }

            // Work out what to do with programme
            int prog_tally_line = -1;
            // Check we actually got a tally-able channel
            if (programId > 0 && programId <= pnlChannelDropDowns.Controls.Count)
            {
                // Channels in the mixer are one indexed, combo boxes in the controlset are zero indexed
                int combo_index = (int)programId - 1;

                // Work out which electrical channel matches this mixer channel (electrical channels zero-indexed)
                // This also helpfully ignores the zero value (blank)
                prog_tally_line = ((ComboBox)pnlChannelDropDowns.Controls[combo_index]).SelectedIndex - 1;

                // And update that label to be red
                ((Label)pnlLampLabels.Controls[combo_index]).BackColor = Color.Red;
            }

            String statusmessage = " Prog: " + programId + "\n Prev: ";
            if (previewId > -1)
            {
                statusmessage += previewId;
            }
            else
            {
                statusmessage += "Not in transition";
            }
            reportMessage(statusmessage);

            // Bail out now if Tally isn't up
            if (!serialTally.IsOpen)
            {
                reportMessage("Tally not up, not updating");
                return;
            }
            
            // Ok, so far, so good. Now turn off everthing already on
            serialTally.WriteLine("<dark>");

            if (-1 != prog_tally_line)
            {
                // Drive programme
                serialTally.Write("<dd" + prog_tally_line.ToString("D2") + ">\n");
            }

            if (-1 != prev_tally_line)
            {
                // Drive preview
                serialTally.Write("<dd" + prev_tally_line.ToString("D2") + ">\n");
            }
        }

        private void buttonConnect_Click(object sender, EventArgs e)
        {
            _BMDSwitcherConnectToFailure failReason = 0;
            string address = textBoxIP.Text;

            try
            {
                // Note that ConnectTo() can take several seconds to return, both for success or failure,
                // depending upon hostname resolution and network response times, so it may be best to
                // do this in a separate thread to prevent the main GUI thread blocking.
                m_switcherDiscovery.ConnectTo(address, out m_switcher, out failReason);
            }
            catch (COMException)
            {
                // An exception will be thrown if ConnectTo fails. For more information, see failReason.
                switch (failReason)
                {
                    case _BMDSwitcherConnectToFailure.bmdSwitcherConnectToFailureNoResponse:
                        reportMessage("No response from Switcher", true);
                        break;
                    case _BMDSwitcherConnectToFailure.bmdSwitcherConnectToFailureIncompatibleFirmware:
                        reportMessage("Switcher has incompatible firmware", true);
                        break;
                    default:
                        reportMessage("Connection failed for unknown reason (is it on?)", true);
                        break;
                }
                return;
            }

            SwitcherConnected();
        }


        /// <summary>
        /// Used for putting other object types into combo boxes.
        /// </summary>
        struct StringObjectPair<T>
        {
            public string name;
            public T value;

            public StringObjectPair(string name, T value)
            {
                this.name = name;
                this.value = value;
            }

            public override string ToString()
            {
                return name;
            }
        }

        private void btnTallyConnect_Click(object sender, EventArgs e)
        {
            serialTally.Close();
            if (null == cmbPort.SelectedItem)
            {
                return;
            }
            serialTally.PortName = (string)cmbPort.SelectedItem;

            try
            {
                serialTally.Open();
                serialTally.WriteLine("<dark>\n");
                UpdateProgramButtonSelection();
                btnTallyConnect.Enabled = false;
            }
            catch (Exception ex)
            {

                reportMessage("Couldn't connect. Error: " + ex.Message, true);
            }
        }

        private void btnLampTest_Click(object sender, EventArgs e)
        {
            if (!serialTally.IsOpen)
            {
                reportMessage("Tally not up!", true);
                return;
            }

            // Turn on every lamp we know about
            for (int i = 0; i < TALLY_CHANNEL_COUNT; i++)
            {
                serialTally.Write("<dd" + i.ToString("D2") + ">\n");
            }

            reportMessage("Testing lamps (see message box)");
            MessageBox.Show("Testing Lamps. Click OK to end");

            serialTally.Write("<dark>\n");

            if (m_mixEffectBlock1 != null)
            {
                UpdateProgramButtonSelection();
            }
        }

        /**
         * If we change a channel routing, trigger an update of everything (sent by changes on all dropdowns) 
         */
        private void cmbTally_SelectedIndexChange(object sender, EventArgs e)
        {
            UpdateProgramButtonSelection();
        }

        /**
         * Report error using message box (rather than modal dialogs)
         */
        private void reportMessage(string errortext, bool iserror = false)
        {
            lblMessageData.Text = errortext;

            if (iserror)
            {
                lblMessageData.ForeColor = Color.Red;
            }
            else
            {
                lblMessageData.ForeColor = Color.Black;
            }
        }
    }
}
