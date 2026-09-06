using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace DungeonPortalBakedBasisPoC.Validation.Tests.Editor
{
    [TestFixture]
    public sealed class DungeonPortalBakedBasisStartMapEnvironmentContractTests
    {
        [Test]
        public void ProductionIndoorContract_FlatBlackIntensityOneFogOff_Passes()
        {
            bool valid = DungeonPortalBakedBasisStartMapRuntimeController
                .TryValidateIndoorRenderContractValuesForTest(
                    true,
                    true,
                    AmbientMode.Flat,
                    new Color(0f, 0f, 0f, 1f),
                    1f,
                    true,
                    false,
                    AmbientMode.Flat,
                    new Color(0f, 0f, 0f, 1f),
                    1f,
                    false,
                    out string failure);

            Assert.That(valid, Is.True, failure);
        }

        [Test]
        public void TrilightCurrentState_CannotBecomeStartMapBaseline()
        {
            bool valid = DungeonPortalBakedBasisStartMapRuntimeController
                .TryValidateIndoorRenderContractValuesForTest(
                    true,
                    true,
                    AmbientMode.Flat,
                    new Color(0f, 0f, 0f, 1f),
                    1f,
                    true,
                    false,
                    AmbientMode.Trilight,
                    new Color(0f, 0f, 0f, 1f),
                    1f,
                    false,
                    out string failure);

            Assert.That(valid, Is.False);
            StringAssert.Contains("RenderSettings", failure);
        }
    }
}
