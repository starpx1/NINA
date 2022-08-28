﻿#region "copyright"

/*
    Copyright © 2016 - 2022 Stefan Berg <isbeorn86+NINA@googlemail.com> and the N.I.N.A. contributors

    This file is part of N.I.N.A. - Nighttime Imaging 'N' Astronomy.

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using FluentAssertions;
using Moq;
using NINA.Sequencer.Utility.DateTimeProvider;
using NINA.Core.Utility;
using NINA.Astrometry;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NINA.Astrometry.RiseAndSet;
using NINA.Astrometry.Interfaces;

namespace NINATest.Sequencer.Utility.DateTimeProvider {

    [TestFixture]
    internal class DawnProviderTest {

        [Test]
        public void GetDateTime_NoDawn_ExceptionIsThrown() {
            var referenceDate = new DateTime(2020, 1, 1, 0, 0, 0);

            var customDateTimeMock = new Mock<ICustomDateTime>();
            customDateTimeMock.SetupGet(x => x.Now).Returns(referenceDate);

            var riseAndSetEvent = new CustomRiseAndSet(referenceDate, 0, 0);
            var nighttimeData = new NighttimeData(referenceDate, referenceDate, AstroUtil.MoonPhase.Unknown, null, riseAndSetEvent, null, null, null);

            var nightTimeCalculatorMock = new Mock<INighttimeCalculator>();
            nightTimeCalculatorMock.Setup(x => x.Calculate(It.IsAny<DateTime?>())).Returns(nighttimeData);

            var sut = new DawnProvider(nightTimeCalculatorMock.Object);
            sut.DateTime = customDateTimeMock.Object;

            Action act = () => sut.GetDateTime(null);
            act.Should().Throw<Exception>();
            nightTimeCalculatorMock.Verify(x => x.Calculate(It.IsAny<DateTime?>()), Times.Once);
        }

        [Test]
        public void GetDateTime_HasSetEvent_SetEventReturned() {
            var referenceDate = new DateTime(2020, 1, 1, 0, 0, 0);
            var customDawn = new DateTime(2020, 2, 2, 2, 2, 2);

            var customDateTimeMock = new Mock<ICustomDateTime>();
            customDateTimeMock.SetupGet(x => x.Now).Returns(referenceDate);

            var riseAndSetEvent = new CustomRiseAndSet(customDawn, null);
            var nighttimeData = new NighttimeData(referenceDate, referenceDate, AstroUtil.MoonPhase.Unknown, null, riseAndSetEvent, null, null, null);

            var nightTimeCalculatorMock = new Mock<INighttimeCalculator>();
            nightTimeCalculatorMock.Setup(x => x.Calculate(It.IsAny<DateTime?>())).Returns(nighttimeData);

            var sut = new DawnProvider(nightTimeCalculatorMock.Object);
            sut.DateTime = customDateTimeMock.Object;

            sut.GetDateTime(null).Should().Be(customDawn);
            nightTimeCalculatorMock.Verify(x => x.Calculate(It.IsAny<DateTime?>()), Times.Once);
        }
    }
}