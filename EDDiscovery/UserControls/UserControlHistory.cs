﻿/*
 * Copyright © 2015 - 2017 EDDiscovery development team
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not use this
 * file except in compliance with the License. You may obtain a copy of the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software distributed under
 * the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF
 * ANY KIND, either express or implied. See the License for the specific language
 * governing permissions and limitations under the License.
 * 
 * EDDiscovery is not affiliated with Frontier Developments plc.
 */
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Diagnostics;
using EliteDangerousCore.EDSM;
using System.Threading.Tasks;
using EDDiscovery.Controls;
using System.Threading;
using System.Collections.Concurrent;
using EliteDangerousCore.EDDN;
using Newtonsoft.Json.Linq;
using EDDiscovery.UserControls;
using EDDiscovery.Forms;
using EliteDangerousCore;
using EliteDangerousCore.DB;

namespace EDDiscovery.UserControls
{
    public partial class UserControlHistory : UserControlCommonBase
    {
        private EDDiscoveryForm discoveryform;

        public HistoryEntry GetTravelHistoryCurrent {  get { return userControlTravelGrid.GetCurrentHistoryEntry; } }

        public UserControlTravelGrid GetTravelGrid { get { return userControlTravelGrid; } }

        public ExtendedControls.TabStrip GetTabStrip( string name )
        {
            if (name.Equals(tabStripBottom.Name, StringComparison.InvariantCultureIgnoreCase))
                return tabStripBottom;
            if (name.Equals(tabStripBottomRight.Name, StringComparison.InvariantCultureIgnoreCase))
                return tabStripBottomRight;
            if (name.Equals(tabStripMiddleRight.Name, StringComparison.InvariantCultureIgnoreCase))
                return tabStripMiddleRight;
            if (name.Equals(tabStripTopRight.Name, StringComparison.InvariantCultureIgnoreCase))
                return tabStripTopRight;
            return null;
        }

        #region Initialisation

        public UserControlHistory()
        {
            InitializeComponent();
        }

        public override void Init(EDDiscoveryForm discoveryForm, UserControlCursorType uctg, int displayno )
        {
            discoveryform = discoveryForm;

            userControlTravelGrid.Init(discoveryform, userControlTravelGrid, displayno);       // primary first instance - this registers with events in discoveryform to get info
                                                        // then this display, to update its own controls..
            userControlTravelGrid.OnChangedSelection += ChangedSelection;   // and if the user clicks on something
            userControlTravelGrid.OnPopOut += () => { discoveryform.PopOuts.PopOut(PopOutControl.PopOuts.TravelGrid); };
            userControlTravelGrid.OnKeyDownInCell += OnKeyDownInCell;
            userControlTravelGrid.ExtraIcons(true, true);

            TabConfigure(tabStripBottom,"Bottom", displayno+1000);          // codes are used to save info, 0 = primary (journal/travelgrid), 1..N are popups, these are embedded UCs
            TabConfigure(tabStripBottomRight,"Bottom-Right", displayno+1001);
            TabConfigure(tabStripMiddleRight, "Middle-Right", displayno+1002);
            TabConfigure(tabStripTopRight, "Top-Right", displayno+1003);
        }

        #endregion

        #region TAB control

        void TabConfigure(ExtendedControls.TabStrip t, string name, int displayno)
        {
            t.ImageList = PopOutControl.GetPopOutImages();
            t.TextList = PopOutControl.GetPopOutToolTips();
            t.Tag = displayno;             // these are IDs for purposes of identifying different instances of a control.. 0 = main ones (main travel grid, main tab journal). 1..N are popups
            t.OnRemoving += TabRemoved;
            t.OnCreateTab += TabCreate;
            t.OnPostCreateTab += TabPostCreate;
            t.OnPopOut += TabPopOut;
            t.Name = name;
        }

        void TabRemoved(ExtendedControls.TabStrip t, Control ctrl )     // called by tab strip when a control is removed
        {
            UserControlCommonBase uccb = ctrl as UserControlCommonBase;
            uccb.Closing();
        }

        Control TabCreate(ExtendedControls.TabStrip t, int si)        // called by tab strip when selected index changes.. create a new one.. only create.
        {
            Control c = PopOutControl.Create(si);
            c.Name = PopOutControl.PopOutList[si].WindowTitlePrefix;        // tabs uses Name field for display, must set it

            discoveryform.ActionRun(Actions.ActionEventEDList.onPanelChange, null, new Conditions.ConditionVariables(new string[] { "PanelTabName", PopOutControl.PopOutList[si].WindowRefName, "PanelTabTitle" , PopOutControl.PopOutList[si].WindowTitlePrefix , "PanelName" , t.Name }));

            return c;
        }

        void TabPostCreate(ExtendedControls.TabStrip t, Control ctrl , int i)        // called by tab strip after control has been added..
        {                                                           // now we can do the configure of it, with the knowledge the tab has the right size
            int displaynumber = (int)t.Tag;                         // tab strip - use tag to remember display id which helps us save context.

            UserControlCommonBase uc = ctrl as UserControlCommonBase;

            if (uc != null)
            {
                uc.Init(discoveryform, userControlTravelGrid, displaynumber);
                uc.LoadLayout();
                uc.InitialDisplay();
            }

            //System.Diagnostics.Debug.WriteLine("And theme {0}", i);
            discoveryform.theme.ApplyToControls(t);
        }

        void TabPopOut(ExtendedControls.TabStrip t, int i)        // pop out clicked
        {
            discoveryform.PopOuts.PopOut(i);
        }

        #endregion


#region Grid Layout

        public override void LoadLayout() 
        {
            // ORDER IMPORTANT for right outer/inner splitter, otherwise windows fixes it 

            if (!EDDOptions.Instance.NoWindowReposition)
            {
                try
                {
                    splitContainerLeftRight.SplitterDistance = SQLiteDBClass.GetSettingInt("TravelControlSpliterLR", splitContainerLeftRight.SplitterDistance);
                    splitContainerLeft.SplitterDistance = SQLiteDBClass.GetSettingInt("TravelControlSpliterL", splitContainerLeft.SplitterDistance);
                    splitContainerRightOuter.SplitterDistance = SQLiteDBClass.GetSettingInt("TravelControlSpliterRO", splitContainerRightOuter.SplitterDistance);
                    splitContainerRightInner.SplitterDistance = SQLiteDBClass.GetSettingInt("TravelControlSpliterR", splitContainerRightInner.SplitterDistance);
                }
                catch { };          // so splitter can except, if values are strange, but we don't really care, so lets throw away the exception
            }

            userControlTravelGrid.LoadLayout();

            // NO NEED to reload the three tabstrips - code below will cause a LoadLayout on the one selected.

            int max = (int)PopOutControl.PopOuts.EndList-1; // fix, its up to but not including endlist

            // saved as the pop out enum value, for historical reasons
            int piindex_bottom = Math.Min(SQLiteDBClass.GetSettingInt("TravelControlBottomTab", (int)(PopOutControl.PopOuts.Scan)), max);
            int piindex_bottomright = Math.Min(SQLiteDBClass.GetSettingInt("TravelControlBottomRightTab", (int)(PopOutControl.PopOuts.Log)), max);
            int piindex_middleright = Math.Min(SQLiteDBClass.GetSettingInt("TravelControlMiddleRightTab", (int)(PopOutControl.PopOuts.StarDistance)), max);
            int piindex_topright = Math.Min(SQLiteDBClass.GetSettingInt("TravelControlTopRightTab", (int)(PopOutControl.PopOuts.SystemInformation)), max);

            tabStripBottom.SelectedIndex = PopOutControl.GetPopOutIndexByEnum((PopOutControl.PopOuts)piindex_bottom);       // translate to image index
            tabStripBottomRight.SelectedIndex = PopOutControl.GetPopOutIndexByEnum((PopOutControl.PopOuts)piindex_bottomright);
            tabStripMiddleRight.SelectedIndex = PopOutControl.GetPopOutIndexByEnum((PopOutControl.PopOuts)piindex_middleright);
            tabStripTopRight.SelectedIndex = PopOutControl.GetPopOutIndexByEnum((PopOutControl.PopOuts)piindex_topright);
        }

        public override void Closing()     // called by form when closing
        {
            userControlTravelGrid.Closing();
            ((UserControlCommonBase)(tabStripBottom.CurrentControl)).Closing();
            ((UserControlCommonBase)(tabStripBottomRight.CurrentControl)).Closing();
            ((UserControlCommonBase)(tabStripMiddleRight.CurrentControl)).Closing();
            ((UserControlCommonBase)(tabStripTopRight.CurrentControl)).Closing();

            SQLiteDBClass.PutSettingInt("TravelControlSpliterLR", splitContainerLeftRight.SplitterDistance);
            SQLiteDBClass.PutSettingInt("TravelControlSpliterL", splitContainerLeft.SplitterDistance);
            SQLiteDBClass.PutSettingInt("TravelControlSpliterRO", splitContainerRightOuter.SplitterDistance);
            SQLiteDBClass.PutSettingInt("TravelControlSpliterR", splitContainerRightInner.SplitterDistance);

            SQLiteDBClass.PutSettingInt("TravelControlBottomRightTab", (int)PopOutControl.PopOutList[tabStripBottomRight.SelectedIndex].popoutid);
            SQLiteDBClass.PutSettingInt("TravelControlBottomTab", (int)PopOutControl.PopOutList[tabStripBottom.SelectedIndex].popoutid);
            SQLiteDBClass.PutSettingInt("TravelControlMiddleRightTab", (int)PopOutControl.PopOutList[tabStripMiddleRight.SelectedIndex].popoutid);
            SQLiteDBClass.PutSettingInt("TravelControlTopRightTab", (int)PopOutControl.PopOutList[tabStripTopRight.SelectedIndex].popoutid);
        }

        #endregion


#region Reaction to UCTG changing

        // history list was repainted, user changed selection, or auto move

        private void ChangedSelection(int rowno, int colno, bool doubleclick, bool note)      // User travel grid call back to say someone clicked somewhere
        {
            if (rowno >= 0)
            {
                HistoryEntry currentsys = userControlTravelGrid.GetCurrentHistoryEntry;

                discoveryform.Map.UpdateHistorySystem(currentsys.System);      // update some dumb friends

                if (userControlTravelGrid.GetCurrentHistoryEntry != null)        // paranoia
                    discoveryform.ActionRun(Actions.ActionEventEDList.onHistorySelection, userControlTravelGrid.GetCurrentHistoryEntry);

                // DEBUG ONLY.. useful for debugging this _discoveryForm.history.SendEDSMStatusInfo(currentsys, true);        // update if required..
            }
        }

        private void OnKeyDownInCell(int keyvalue, int rowno, int colno, bool note)
        {
            if (note)
            {
                UserControlSysInfo si = tabStripTopRight.CurrentControl as UserControlSysInfo;
                if (si == null || !si.IsNotesShowing)
                    si = tabStripMiddleRight.CurrentControl as UserControlSysInfo;
                if (si == null || !si.IsNotesShowing)
                    si = tabStripBottomRight.CurrentControl as UserControlSysInfo;

                if (si != null && si.IsNotesShowing)      // if its note, and we have a system info window
                {
                    si.FocusOnNote(keyvalue);
                }
            }
        }

#endregion
    }
}