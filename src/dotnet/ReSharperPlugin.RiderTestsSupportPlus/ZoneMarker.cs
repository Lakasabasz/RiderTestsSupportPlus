using JetBrains.Application.BuildScript.Application.Zones;
using JetBrains.ReSharper.UnitTestFramework;

namespace ReSharperPlugin.RiderTestsSupportPlus;

[ZoneMarker]
public class ZoneMarker : IRequire<IUnitTestingZone>;
