#region "copyright"

/*
    Copyright � 2016 - 2024 Stefan Berg <isbeorn86+NINA@googlemail.com> and the N.I.N.A. contributors

    This file is part of N.I.N.A. - Nighttime Imaging 'N' Astronomy.

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Equipment.Equipment.MyFilterWheel;
using NINA.Equipment.Interfaces.ViewModel;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Equipment.Interfaces.Mediator {

    public interface IFilterWheelMediator : IDeviceMediator<IFilterWheelVM, IFilterWheelConsumer, FilterWheelInfo> {

        Task<FilterInfo> ChangeFilter(FilterInfo inputFilter, CancellationToken token = new CancellationToken(), IProgress<ApplicationStatus> progress = null);
        event Func<object, FilterChangedEventArgs, Task> FilterChanged;
    }

    public class FilterChangedEventArgs : EventArgs {
        public FilterChangedEventArgs(FilterInfo from, FilterInfo to) {
            From = from;
            To = to;
        }

        public FilterInfo From { get; }
        public FilterInfo To { get; }
    }
}