package pl.info.lukaszm.plugins.ridertestssupportplus.test

import com.jetbrains.rider.test.framework.testData.IRiderTestDataMarker
import java.nio.file.Path

// Found by the Rider test framework by name, in the package of the tests or above.
@Suppress("unused")
object RiderTestDataMarker : IRiderTestDataMarker {
    override val testDataFromRoot: Path
        get() = Path.of("src/test/testData")
}
