﻿using NINA.Utility.Enum;

namespace NINA.Model.MyPlanetarium {

    public interface IPlanetariumFactory {

        IPlanetarium GetPlanetarium();

        IPlanetarium GetPlanetarium(PlanetariumEnum planetarium);
    }
}