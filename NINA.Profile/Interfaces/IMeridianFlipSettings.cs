#region "copyright"

/*
    Copyright � 2016 - 2022 Stefan Berg <isbeorn86+NINA@googlemail.com> and the N.I.N.A. contributors

    This file is part of N.I.N.A. - Nighttime Imaging 'N' Astronomy.

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

namespace NINA.Profile.Interfaces {

    public interface IMeridianFlipSettings : ISettings {

        /// <summary>
        /// The earliest time a flip should happen
        /// </summary>
        double MinutesAfterMeridian { get; set; }

        /// <summary>
        /// The latest time a flip should happen
        /// </summary>
        double MaxMinutesAfterMeridian { get; set; }

        double PauseTimeBeforeMeridian { get; set; }
        bool Recenter { get; set; }
        int SettleTime { get; set; }
        bool UseSideOfPier { get; set; }
        bool AutoFocusAfterFlip { get; set; }
        bool RotateImageAfterFlip { get; set; }
    }
}