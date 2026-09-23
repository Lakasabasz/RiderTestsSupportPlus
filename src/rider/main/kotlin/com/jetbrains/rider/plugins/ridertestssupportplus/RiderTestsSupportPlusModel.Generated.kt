@file:Suppress("EXPERIMENTAL_API_USAGE","EXPERIMENTAL_UNSIGNED_LITERALS","PackageDirectoryMismatch","UnusedImport","unused","LocalVariableName","CanBeVal","PropertyName","EnumEntryName","ClassName","ObjectPropertyName","UnnecessaryVariable","SpellCheckingInspection")
package com.jetbrains.rd.ide.model

import com.jetbrains.rd.framework.*
import com.jetbrains.rd.framework.base.*
import com.jetbrains.rd.framework.impl.*

import com.jetbrains.rd.util.lifetime.*
import com.jetbrains.rd.util.reactive.*
import com.jetbrains.rd.util.string.*
import com.jetbrains.rd.util.*
import kotlin.time.Duration
import kotlin.reflect.KClass
import kotlin.jvm.JvmStatic



/**
 * #### Generated from [RiderTestsSupportPlusModel.kt:11]
 */
class RiderTestsSupportPlusModel private constructor(
    private val _saveSession: RdCall<SaveSessionRequest, Int>,
    private val _loadSession: RdCall<String, SessionReport>,
    private val _importRunSettings: RdCall<String, SessionReport>
) : RdExtBase() {
    //companion
    
    companion object : ISerializersOwner {
        
        override fun registerSerializersCore(serializers: ISerializers)  {
            val classLoader = javaClass.classLoader
            serializers.register(LazyCompanionMarshaller(RdId(-2982540748054584809), classLoader, "com.jetbrains.rd.ide.model.SessionReport"))
            serializers.register(LazyCompanionMarshaller(RdId(-3342547550303174807), classLoader, "com.jetbrains.rd.ide.model.SaveSessionRequest"))
        }
        
        
        
        
        
        const val serializationHash = -4442246916081668415L
        
    }
    override val serializersOwner: ISerializersOwner get() = RiderTestsSupportPlusModel
    override val serializationHash: Long get() = RiderTestsSupportPlusModel.serializationHash
    
    //fields
    val saveSession: IRdCall<SaveSessionRequest, Int> get() = _saveSession
    val loadSession: IRdCall<String, SessionReport> get() = _loadSession
    val importRunSettings: IRdCall<String, SessionReport> get() = _importRunSettings
    //methods
    //initializer
    init {
        bindableChildren.add("saveSession" to _saveSession)
        bindableChildren.add("loadSession" to _loadSession)
        bindableChildren.add("importRunSettings" to _importRunSettings)
    }
    
    //secondary constructor
    internal constructor(
    ) : this(
        RdCall<SaveSessionRequest, Int>(SaveSessionRequest, FrameworkMarshallers.Int),
        RdCall<String, SessionReport>(FrameworkMarshallers.String, SessionReport),
        RdCall<String, SessionReport>(FrameworkMarshallers.String, SessionReport)
    )
    
    //equals trait
    //hash code trait
    //pretty print
    override fun print(printer: PrettyPrinter)  {
        printer.println("RiderTestsSupportPlusModel (")
        printer.indent {
            print("saveSession = "); _saveSession.print(printer); println()
            print("loadSession = "); _loadSession.print(printer); println()
            print("importRunSettings = "); _importRunSettings.print(printer); println()
        }
        printer.print(")")
    }
    //deepClone
    override fun deepClone(): RiderTestsSupportPlusModel   {
        return RiderTestsSupportPlusModel(
            _saveSession.deepClonePolymorphic(),
            _loadSession.deepClonePolymorphic(),
            _importRunSettings.deepClonePolymorphic()
        )
    }
    //contexts
    //threading
    override val extThreading: ExtThreadingKind get() = ExtThreadingKind.Default
}
val Solution.riderTestsSupportPlusModel get() = getOrCreateExtension("riderTestsSupportPlusModel", ::RiderTestsSupportPlusModel)



/**
 * #### Generated from [RiderTestsSupportPlusModel.kt:24]
 */
data class SaveSessionRequest (
    val sessionName: String?,
    val path: String
) : IPrintable {
    //write-marshaller
    private fun write(ctx: SerializationCtx, buffer: AbstractBuffer)  {
        buffer.writeNullable(sessionName) { buffer.writeString(it) }
        buffer.writeString(path)
    }
    //companion
    
    companion object : IMarshaller<SaveSessionRequest> {
        override val _type: KClass<SaveSessionRequest> = SaveSessionRequest::class
        override val id: RdId get() = RdId(-3342547550303174807)
        
        @Suppress("UNCHECKED_CAST")
        override fun read(ctx: SerializationCtx, buffer: AbstractBuffer): SaveSessionRequest  {
            val sessionName = buffer.readNullable { buffer.readString() }
            val path = buffer.readString()
            return SaveSessionRequest(sessionName, path)
        }
        
        override fun write(ctx: SerializationCtx, buffer: AbstractBuffer, value: SaveSessionRequest)  {
            value.write(ctx, buffer)
        }
        
        
    }
    //fields
    //methods
    //initializer
    //secondary constructor
    //equals trait
    override fun equals(other: Any?): Boolean  {
        if (this === other) return true
        if (other == null || other::class != this::class) return false
        
        other as SaveSessionRequest
        
        if (sessionName != other.sessionName) return false
        if (path != other.path) return false
        
        return true
    }
    //hash code trait
    override fun hashCode(): Int  {
        var __r = 0
        __r = __r*31 + if (sessionName != null) sessionName.hashCode() else 0
        __r = __r*31 + path.hashCode()
        return __r
    }
    //pretty print
    override fun print(printer: PrettyPrinter)  {
        printer.println("SaveSessionRequest (")
        printer.indent {
            print("sessionName = "); sessionName.print(printer); println()
            print("path = "); path.print(printer); println()
        }
        printer.print(")")
    }
    //deepClone
    //contexts
    //threading
}


/**
 * #### Generated from [RiderTestsSupportPlusModel.kt:13]
 */
data class SessionReport (
    val sessionName: String?,
    val exact: List<String>,
    val ancestors: List<String>,
    val unresolved: List<String>,
    val notes: List<String>,
    val rescanned: Boolean
) : IPrintable {
    //write-marshaller
    private fun write(ctx: SerializationCtx, buffer: AbstractBuffer)  {
        buffer.writeNullable(sessionName) { buffer.writeString(it) }
        buffer.writeList(exact) { v -> buffer.writeString(v) }
        buffer.writeList(ancestors) { v -> buffer.writeString(v) }
        buffer.writeList(unresolved) { v -> buffer.writeString(v) }
        buffer.writeList(notes) { v -> buffer.writeString(v) }
        buffer.writeBool(rescanned)
    }
    //companion
    
    companion object : IMarshaller<SessionReport> {
        override val _type: KClass<SessionReport> = SessionReport::class
        override val id: RdId get() = RdId(-2982540748054584809)
        
        @Suppress("UNCHECKED_CAST")
        override fun read(ctx: SerializationCtx, buffer: AbstractBuffer): SessionReport  {
            val sessionName = buffer.readNullable { buffer.readString() }
            val exact = buffer.readList { buffer.readString() }
            val ancestors = buffer.readList { buffer.readString() }
            val unresolved = buffer.readList { buffer.readString() }
            val notes = buffer.readList { buffer.readString() }
            val rescanned = buffer.readBool()
            return SessionReport(sessionName, exact, ancestors, unresolved, notes, rescanned)
        }
        
        override fun write(ctx: SerializationCtx, buffer: AbstractBuffer, value: SessionReport)  {
            value.write(ctx, buffer)
        }
        
        
    }
    //fields
    //methods
    //initializer
    //secondary constructor
    //equals trait
    override fun equals(other: Any?): Boolean  {
        if (this === other) return true
        if (other == null || other::class != this::class) return false
        
        other as SessionReport
        
        if (sessionName != other.sessionName) return false
        if (exact != other.exact) return false
        if (ancestors != other.ancestors) return false
        if (unresolved != other.unresolved) return false
        if (notes != other.notes) return false
        if (rescanned != other.rescanned) return false
        
        return true
    }
    //hash code trait
    override fun hashCode(): Int  {
        var __r = 0
        __r = __r*31 + if (sessionName != null) sessionName.hashCode() else 0
        __r = __r*31 + exact.hashCode()
        __r = __r*31 + ancestors.hashCode()
        __r = __r*31 + unresolved.hashCode()
        __r = __r*31 + notes.hashCode()
        __r = __r*31 + rescanned.hashCode()
        return __r
    }
    //pretty print
    override fun print(printer: PrettyPrinter)  {
        printer.println("SessionReport (")
        printer.indent {
            print("sessionName = "); sessionName.print(printer); println()
            print("exact = "); exact.print(printer); println()
            print("ancestors = "); ancestors.print(printer); println()
            print("unresolved = "); unresolved.print(printer); println()
            print("notes = "); notes.print(printer); println()
            print("rescanned = "); rescanned.print(printer); println()
        }
        printer.print(")")
    }
    //deepClone
    //contexts
    //threading
}
