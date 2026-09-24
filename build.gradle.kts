import com.jetbrains.plugin.structure.base.utils.isFile
import groovy.ant.FileNameFinder
import org.apache.tools.ant.taskdefs.condition.Os
import org.jetbrains.intellij.platform.gradle.Constants
import org.jetbrains.intellij.platform.gradle.tasks.PrepareSandboxTask
import org.gradle.api.tasks.testing.logging.TestExceptionFormat

plugins {
    id("java")
    alias(libs.plugins.kotlinJvm)
    id("org.jetbrains.intellij.platform") version "2.19.0"     // See https://github.com/JetBrains/intellij-platform-gradle-plugin/releases
    id("me.filippov.gradle.jvm.wrapper") version "0.15.0"
}

val isWindows = Os.isFamily(Os.FAMILY_WINDOWS)
extra["isWindows"] = isWindows

val DotnetSolution: String by project
val BuildConfiguration: String by project
val ProductVersion: String by project
val DotnetPluginId: String by project
val RiderPluginId: String by project
val PublishToken: String by project

allprojects {
    repositories {
        maven { setUrl("https://cache-redirector.jetbrains.com/maven-central") }
    }
}

repositories {
    intellijPlatform {
        defaultRepositories()
        jetbrainsRuntime()
    }
}

version = extra["PluginVersion"] as String

tasks.processResources {
    from("dependencies.json") { into("META-INF") }
}

sourceSets {
    main {
        java.srcDir("src/rider/main/java")
        kotlin.srcDir("src/rider/main/kotlin")
        resources.srcDir("src/rider/main/resources")
    }
    test {
        kotlin.srcDir("src/test/kotlin")
    }
}

var buildToolExecutable: String? = null
var buildToolArgs: List<String>? = null

val setBuildTool by tasks.registering {
    doLast {
        var executable = "dotnet"
        var args = mutableListOf("msbuild")

        if (isWindows) {
            val execResult = providers.exec {
                executable("${rootDir}\\tools\\vswhere.exe")
                args("-latest", "-property", "installationPath", "-products", "*")
                workingDir(rootDir)
            }

            val directory = execResult.standardOutput.asText.get().trim()
            if (directory.isNotEmpty()) {
                val files = FileNameFinder().getFileNames("${directory}\\MSBuild", "**/MSBuild.exe")
                executable = files.get(0)
                args = mutableListOf("/v:minimal")
            }
        }

        args.add(DotnetSolution)
        args.add("/p:Configuration=${BuildConfiguration}")
        args.add("/p:HostFullIdentifier=")

        buildToolExecutable = executable
        buildToolArgs = args
    }
}

val compileDotNet by tasks.registering {
    dependsOn(setBuildTool)
    doLast {
        val arguments = buildToolArgs!!.toMutableList()
        arguments.add("/t:Restore;Rebuild")
        providers.exec {
            executable(buildToolExecutable!!)
            args(arguments)
            workingDir(rootDir)
        }.result.get()
    }
}

val testDotNet by tasks.registering {
    doLast {
        providers.exec {
            executable("dotnet")
            args("test","${DotnetSolution}","--logger","GitHubActions")
            workingDir(rootDir)
        }.result.get()
    }
}

tasks.buildPlugin {
    doLast {
        copy {
            from("${buildDir}/distributions/${rootProject.name}-${version}.zip")
            into("${rootDir}/output")
        }

        // TODO: See also org.jetbrains.changelog: https://github.com/JetBrains/gradle-changelog-plugin
        val changelogText = file("${rootDir}/CHANGELOG.md").readText()
        val changelogMatches = Regex("(?s)(-.+?)(?=##|$)").findAll(changelogText)
        val changeNotes = changelogMatches.map {
            it.groups[1]!!.value.replace("(?s)- ".toRegex(), "\u2022 ").replace("`", "").replace(",", "%2C").replace(";", "%3B")
        }.take(1).joinToString()

        val arguments = buildToolArgs!!.toMutableList()
        arguments.add("/t:Pack")
        arguments.add("/p:PackageOutputPath=${rootDir}/output")
        arguments.add("/p:PackageReleaseNotes=${changeNotes}")
        arguments.add("/p:PackageVersion=${version}")
        providers.exec {
            executable(buildToolExecutable!!)
            args(arguments)
            workingDir(rootDir)
        }.result.get()
    }
}

dependencies {
    intellijPlatform {
        // RiderSdkPath (or RIDER_SDK_PATH): an already extracted Rider, e.g. baked into the CI image (see ci/Dockerfile).
        // Otherwise the multi-platform Rider is downloaded from Maven (~4 GB, ~10 GB extracted in the Gradle cache).
        val riderSdkPath = providers.gradleProperty("RiderSdkPath").orElse(providers.environmentVariable("RIDER_SDK_PATH")).orNull
        if (riderSdkPath != null) {
            local(riderSdkPath)
        } else {
            rider(ProductVersion) {
                useInstaller = false
            }
        }
        jetbrainsRuntime()

        // Base classes of the Unit Tests window actions
        bundledModule("intellij.rider")
        bundledModule("intellij.rider.rdclient.dotnet")
        bundledModule("intellij.rd.client")

        // Rider integration test framework (headless Rider with backend and a real solution)
        bundledLibrary(provider {
            project.intellijPlatform.platformPath.resolve("lib/testFramework.jar").toString()
        })
    }
}

dependencies {
    testImplementation(platform("org.junit:junit-bom:5.13.4"))
    testImplementation("org.junit.jupiter:junit-jupiter")
    testRuntimeOnly("org.junit.platform:junit-platform-launcher")
    // The Rider test framework also references JUnit 4 rules
    testImplementation("junit:junit:4.13.2")
    testImplementation(kotlin("test"))
}

tasks.test {
    // The tests exercise the backend assembly in the sandbox, which Gradle does not track as a test input
    outputs.upToDateWhen { false }
    // .NET images set DOTNET_VERSION to the runtime version (e.g. 10.0.12); the Rider test framework takes it
    // as the SDK version to download, which does not exist
    environment.remove("DOTNET_VERSION")
    // Ignore IJ Platform JUnit5 framework set up and tear down
    systemProperty("intellij.build.test.ignoreFirstAndLastTests", "true")
    useJUnitPlatform()
    testLogging {
        showStandardStreams = true
        exceptionFormat = TestExceptionFormat.FULL
    }
}

tasks.runIde {
    // Match Rider's default heap size of 1.5Gb (default for runIde is 512Mb)
    maxHeapSize = "1500m"
}

tasks.patchPluginXml {
    // TODO: See also org.jetbrains.changelog: https://github.com/JetBrains/gradle-changelog-plugin
    val changelogText = file("${rootDir}/CHANGELOG.md").readText()
    val changelogMatches = Regex("(?s)(-.+?)(?=##|\$)").findAll(changelogText)

    changeNotes.set(changelogMatches.map {
        it.groups[1]!!.value.replace("(?s)\r?\n".toRegex(), "<br />\n")
    }.take(1).joinToString())
}

// Both runIde and test sandboxes need the backend assembly
tasks.withType<PrepareSandboxTask>().configureEach {
    dependsOn(compileDotNet)

    val outputFolder = "${rootDir}/src/dotnet/${DotnetPluginId}/bin/${DotnetPluginId}.Rider/${BuildConfiguration}"
    val dllFiles = listOf(
            "$outputFolder/${DotnetPluginId}.dll",
            "$outputFolder/${DotnetPluginId}.pdb",
            // Lists tests selected by <NUnit><Where> (runs next to the backend assembly, see NUnitWhereListing.cs)
            "${rootDir}/src/tools/RiderTestsSupportPlus.NUnitLister/bin/${BuildConfiguration}/RiderTestsSupportPlus.NUnitLister.dll",

            // TODO: add additional assemblies
    )

    dllFiles.forEach { f ->
        val file = file(f)
        from(file) { into("${rootProject.name}/dotnet") }
    }

    doLast {
        dllFiles.forEach { f ->
            val file = file(f)
            if (!file.exists()) throw RuntimeException("File $file does not exist")
        }
    }
}

tasks.publishPlugin {
    dependsOn(testDotNet)
    dependsOn(tasks.buildPlugin)
    token.set("${PublishToken}")

    doLast {
        providers.exec {
            executable("dotnet")
            args("nuget","push","output/${DotnetPluginId}.${version}.nupkg","--api-key","${PublishToken}","--source","https://plugins.jetbrains.com")
            workingDir(rootDir)
        }.result.get()
    }
}

val riderModel: Configuration by configurations.creating {
    isCanBeConsumed = true
    isCanBeResolved = false
}

artifacts {
    add(riderModel.name, provider {
        intellijPlatform.platformPath.resolve("lib/rd/rider-model.jar").also {
            check(it.isFile) {
                "rider-model.jar is not found at $riderModel"
            }
        }
    }) {
        builtBy(Constants.Tasks.INITIALIZE_INTELLIJ_PLATFORM_PLUGIN)
    }
}
