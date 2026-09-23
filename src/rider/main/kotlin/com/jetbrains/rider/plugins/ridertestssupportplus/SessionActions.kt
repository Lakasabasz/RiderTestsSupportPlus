package com.jetbrains.rider.plugins.ridertestssupportplus

import com.jetbrains.rider.unitTesting.actions.base.RiderUnitTestAnActionBase
import com.jetbrains.rider.unitTesting.actions.targets.RiderUnitTestTarget
import com.jetbrains.rider.unitTesting.actions.targets.RiderUnitTestTargetExecutor
import com.jetbrains.rider.unitTesting.actions.targets.RiderUnitTestTargetOperation
import com.jetbrains.rider.unitTesting.actions.targets.RiderUnitTestTargetScope

// Thin proxies: Rider forwards the action, with the Unit Tests window's data context, to the backend action
// of the given id (see Actions/SessionActions.cs). Keep the ids in sync.

class SaveSessionAction : RiderUnitTestAnActionBase("RiderTestsSupportPlus.SaveSession") {
    override val target = RiderUnitTestTarget(
        RiderUnitTestTargetOperation.General, RiderUnitTestTargetExecutor.None, RiderUnitTestTargetScope.SelectedSession)
}

class LoadSessionAction : RiderUnitTestAnActionBase("RiderTestsSupportPlus.LoadSession") {
    override val target = RiderUnitTestTarget(
        RiderUnitTestTargetOperation.General, RiderUnitTestTargetExecutor.None, RiderUnitTestTargetScope.None)
}

class ImportRunSettingsAction : RiderUnitTestAnActionBase("RiderTestsSupportPlus.ImportRunSettings") {
    override val target = RiderUnitTestTarget(
        RiderUnitTestTargetOperation.General, RiderUnitTestTargetExecutor.None, RiderUnitTestTargetScope.None)
}
