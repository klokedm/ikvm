/*
  Copyright (C) 2002-2015 Jeroen Frijters

  This software is provided 'as-is', without any express or implied
  warranty.  In no event will the authors be held liable for any damages
  arising from the use of this software.

  Permission is granted to anyone to use this software for any purpose,
  including commercial applications, and to alter it and redistribute it
  freely, subject to the following restrictions:

  1. The origin of this software must not be misrepresented; you must not
     claim that you wrote the original software. If you use this software
     in a product, an acknowledgment in the product documentation would be
     appreciated but is not required.
  2. Altered source versions must be plainly marked as such, and must not be
     misrepresented as being the original software.
  3. This notice may not be removed or altered from any source distribution.

  Jeroen Frijters
  jeroen@frijters.net
  
*/
using System;
#if STATIC_COMPILER || STUB_GENERATOR
using IKVM.Reflection;
using IKVM.Reflection.Emit;
using Type = IKVM.Reflection.Type;
using ProtectionDomain = System.Object;
#else
using System.Reflection;
using System.Reflection.Emit;
using ProtectionDomain = java.security.ProtectionDomain;
#endif
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Runtime.CompilerServices;
using IKVM.Attributes;
using System.Linq;

namespace IKVM.Internal
{
    ////////////////////////////////////////////////////////////////////////////////////////////////////
    /// <summary>   A bit-field of flags for specifying code Generate options. </summary>
    ///
    /// <remarks>   Semantika d.o.o.,. </remarks>
    ////////////////////////////////////////////////////////////////////////////////////////////////////
    [Flags]
    enum CodeGenOptions
    {
        /// <summary>   A binary constant representing the none flag. </summary>
        None = 0,
        Debug = 1,
        NoStackTraceInfo = 2,
        StrictFinalFieldSemantics = 4,
        NoJNI = 8,
        RemoveAsserts = 16,
        NoAutomagicSerialization = 32,
        DisableDynamicBinding = 64,
        NoRefEmitHelpers = 128,
        RemoveUnusedFields = 256,
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    /// <summary>   A bit-field of flags for specifying load modes. </summary>
    ///
    /// <remarks>   Semantika d.o.o.,. </remarks>
    ////////////////////////////////////////////////////////////////////////////////////////////////////
    [Flags]
    enum LoadMode
    {
        // These are the modes that should be used
        /// <summary>   . </summary>
        Find = ReturnNull,
        LoadOrNull = Load | ReturnNull,
        LoadOrThrow = Load | ThrowClassNotFound,
        Link = Load | ReturnUnloadable | SuppressExceptions,

        /// <summary>   A binary constant representing the load flag. </summary>
        // call into Java class loader
        Load = 0x0001,

        // return value
        /// <summary>   This is used with a bitwise OR to disable returning unloadable. </summary>
        DontReturnUnloadable = 0x0002,
        ReturnUnloadable = 0x0004,
        ReturnNull = 0x0004 | DontReturnUnloadable,
        ThrowClassNotFound = 0x0008 | DontReturnUnloadable,
        MaskReturn = ReturnUnloadable | ReturnNull | ThrowClassNotFound,

        /// <summary>   A binary constant representing the suppress exceptions flag. </summary>
        // exceptions (not ClassNotFoundException)
        SuppressExceptions = 0x0010,

        /// <summary>   A binary constant representing the Warning class not found flag. </summary>
        // warnings
        WarnClassNotFound = 0x0020,
    }

#if !STUB_GENERATOR


    ////////////////////////////////////////////////////////////////////////////////////////////////////
    /// <summary>   A type wrapper factory. </summary>
    ///
    /// <remarks>   Semantika d.o.o.,. </remarks>
    ////////////////////////////////////////////////////////////////////////////////////////////////////
    abstract class TypeWrapperFactory
    {
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Gets the module builder. </summary>
        ///
        /// <value> The module builder. </value>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal abstract ModuleBuilder ModuleBuilder { get; }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Define class implementation. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="types">            The types. </param>
        /// <param name="host">             The host. </param>
        /// <param name="f">                A ClassFile to process. </param>
        /// <param name="classLoader">      The class loader. </param>
        /// <param name="protectionDomain"> The protection domain. </param>
        ///
        /// <returns>   A TypeWrapper. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal abstract TypeWrapper DefineClassImpl(Dictionary<string, TypeWrapper> types, TypeWrapper host, ClassFile f, ClassLoaderWrapper classLoader, ProtectionDomain protectionDomain);

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Reserve name. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="name"> The name. </param>
        ///
        /// <returns>   True if it succeeds, false if it fails. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal abstract bool ReserveName(string name);

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Allocate mangled name. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="tw">   The tw. </param>
        ///
        /// <returns>   A string. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal abstract string AllocMangledName(DynamicTypeWrapper tw);

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Define unloadable. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="name"> The name. </param>
        ///
        /// <returns>   A Type. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal abstract Type DefineUnloadable(string name);

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Define delegate. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="parameterCount">   Number of parameters. </param>
        /// <param name="returnVoid">       True to return void. </param>
        ///
        /// <returns>   A Type. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal abstract Type DefineDelegate(int parameterCount, bool returnVoid);

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Gets a value indicating whether this instance has internal access. </summary>
        ///
        /// <value> True if this instance has internal access, false if not. </value>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal abstract bool HasInternalAccess { get; }
#if CLASSGC
		internal abstract void AddInternalsVisibleTo(Assembly friend);
#endif
    }
#endif // !STUB_GENERATOR

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    /// <summary>   The class loader wrapper. </summary>
    ///
    /// <remarks>   Semantika d.o.o.,. </remarks>
    ////////////////////////////////////////////////////////////////////////////////////////////////////
    class ClassLoaderWrapper
    {
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   In order to compare the types correctly, we let the framework determine the system type, which automatically resolves the type according to the forwarding rules
        ///             TODO: this needs to be reviewed! </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="sourceType">   Type of the source. </param>
        ///
        /// <returns>   The system type. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        private static System.Type GetSystemType(Type sourceType)
        {
#if STATIC_COMPILER || STUB_GENERATOR
            try {
                var loadedType = System.Type.GetType(sourceType.AssemblyQualifiedName);
                return loadedType;
            } catch (Exception ex) {
                return null;
            }
#else
            return sourceType;
#endif
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>
        /// A type forwarding comparer - due to the way .NET Core uses reference assemblies we need to
        /// not only compare the types directly but verify if one of them forwards to the other as we
        /// need to treat them as the same type when checking for re-mappings.
        /// 
        /// We do this by letting the framework load the correct system type, which will also resolve any
        /// forwarders. 
        /// 
        /// TODO: We either need to make sure the type loading happens correctly *or* we need to add the
        /// forwarding infrastructure to the IKVM reflection logic.
        /// </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal class TypeForwardingComparer : IEqualityComparer<Type>
        {



            ////////////////////////////////////////////////////////////////////////////////////////////////////
            /// <summary>   Determines whether the specified objects are equal. </summary>
            ///
            /// <remarks>   Semantika d.o.o.,. </remarks>
            ///
            /// <param name="x">    The first object of type <paramref name="T" /> to compare. </param>
            /// <param name="y">    The second object of type <paramref name="T" /> to compare. </param>
            ///
            /// <returns>
            /// <see langword="true" /> if the specified objects are equal; otherwise,
            /// <see langword="false" />.
            /// </returns>
            ////////////////////////////////////////////////////////////////////////////////////////////////////
            public bool Equals(Type x, Type y)
            {
                if (x.Equals(y))
                {
                    //The IKVM types match, no need to try a system based type resolution
                    return true;
                }
                //They did not match, try a system based resolution
                var xSystemType = GetSystemType(x);
                var ySystemType = GetSystemType(y);

                return xSystemType != null && ySystemType != null && xSystemType.Equals(ySystemType);
            }

            ////////////////////////////////////////////////////////////////////////////////////////////////////
            /// <summary>   Returns a hash code for the specified object. </summary>
            ///
            /// <remarks>   Semantika d.o.o.,. </remarks>
            ///
            /// <param name="obj">  The <see cref="T:System.Object" /> for which a hash code is to be
            ///                     returned. </param>
            ///
            /// <returns>   A hash code for the specified object. </returns>
            ////////////////////////////////////////////////////////////////////////////////////////////////////
            public int GetHashCode(Type obj)
            {
                //Try to get the system type. 
                //If we could get it, use the hash code of the system type, otherwise fallback to the internal implementation.
                var sysType = GetSystemType(obj);
                return sysType == null ? obj.GetHashCode() : sysType.GetHashCode();
            }
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   The wrapper lock. </summary>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        private static readonly object wrapperLock = new object();

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   The global type to type wrapper. </summary>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        private static readonly Dictionary<Type, TypeWrapper> globalTypeToTypeWrapper = new Dictionary<Type, TypeWrapper>();
#if STATIC_COMPILER || STUB_GENERATOR
        private static ClassLoaderWrapper bootstrapClassLoader;
#else


        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   The bootstrap class loader. </summary>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        private static AssemblyClassLoader bootstrapClassLoader;
#endif

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   The generic class loaders. </summary>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        private static List<GenericClassLoaderWrapper> genericClassLoaders;
#if !STATIC_COMPILER && !FIRST_PASS && !STUB_GENERATOR


        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   The java class loader. </summary>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        protected java.lang.ClassLoader javaClassLoader;
#endif
#if !STUB_GENERATOR


        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   The factory. </summary>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        private TypeWrapperFactory factory;
#endif // !STUB_GENERATOR

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   The types. </summary>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        private readonly Dictionary<string, TypeWrapper> types = new Dictionary<string, TypeWrapper>();

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   The define class in progress. </summary>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        private readonly Dictionary<string, Thread> defineClassInProgress = new Dictionary<string, Thread>();

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   The native libraries. </summary>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        private List<IntPtr> nativeLibraries;

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   The codegenoptions. </summary>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        private readonly CodeGenOptions codegenoptions;
#if CLASSGC
		private Dictionary<Type, TypeWrapper> typeToTypeWrapper;
		private static ConditionalWeakTable<Assembly, ClassLoaderWrapper> dynamicAssemblies;
#endif

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   List of types of the remapped. </summary>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        private static readonly Dictionary<Type, string> remappedTypes = new Dictionary<Type, string>(new TypeForwardingComparer());

#if STATIC_COMPILER || STUB_GENERATOR
        // HACK this is used by the ahead-of-time compiler to overrule the bootstrap classloader
        // when we're compiling the core class libraries and by ikvmstub with the -bootstrap option
        internal static void SetBootstrapClassLoader(ClassLoaderWrapper bootstrapClassLoader)
        {
            Debug.Assert(ClassLoaderWrapper.bootstrapClassLoader == null);

            ClassLoaderWrapper.bootstrapClassLoader = bootstrapClassLoader;
        }
#endif

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Static constructor. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        static ClassLoaderWrapper()
        {
            globalTypeToTypeWrapper[PrimitiveTypeWrapper.BOOLEAN.TypeAsTBD] = PrimitiveTypeWrapper.BOOLEAN;
            globalTypeToTypeWrapper[PrimitiveTypeWrapper.BYTE.TypeAsTBD] = PrimitiveTypeWrapper.BYTE;
            globalTypeToTypeWrapper[PrimitiveTypeWrapper.CHAR.TypeAsTBD] = PrimitiveTypeWrapper.CHAR;
            globalTypeToTypeWrapper[PrimitiveTypeWrapper.DOUBLE.TypeAsTBD] = PrimitiveTypeWrapper.DOUBLE;
            globalTypeToTypeWrapper[PrimitiveTypeWrapper.FLOAT.TypeAsTBD] = PrimitiveTypeWrapper.FLOAT;
            globalTypeToTypeWrapper[PrimitiveTypeWrapper.INT.TypeAsTBD] = PrimitiveTypeWrapper.INT;
            globalTypeToTypeWrapper[PrimitiveTypeWrapper.LONG.TypeAsTBD] = PrimitiveTypeWrapper.LONG;
            globalTypeToTypeWrapper[PrimitiveTypeWrapper.SHORT.TypeAsTBD] = PrimitiveTypeWrapper.SHORT;
            globalTypeToTypeWrapper[PrimitiveTypeWrapper.VOID.TypeAsTBD] = PrimitiveTypeWrapper.VOID;
            LoadRemappedTypes();
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Loads remapped types. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <exception cref="FatalCompilerErrorException">  Thrown when a Fatal Compiler Error error
        ///                                                 condition occurs. </exception>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal static void LoadRemappedTypes()
        {
            // if we're compiling the core, coreAssembly will be null
            Assembly coreAssembly = JVM.CoreAssembly;
            if (coreAssembly != null && remappedTypes.Count == 0)
            {
                RemappedClassAttribute[] remapped = AttributeHelper.GetRemappedClasses(coreAssembly);
                if (remapped.Length > 0)
                {
                    foreach (RemappedClassAttribute r in remapped)
                    {
                        remappedTypes.Add(r.RemappedType, r.Name);
                    }
                }
                else
                {
#if STATIC_COMPILER
					throw new FatalCompilerErrorException(Message.CoreClassesMissing);
#else
                    JVM.CriticalFailure("Failed to find core classes in core library", null);
#endif
                }
            }
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Constructor. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="codegenoptions">   The codegenoptions. </param>
        /// <param name="javaClassLoader">  The java class loader. </param>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal ClassLoaderWrapper(CodeGenOptions codegenoptions, object javaClassLoader)
        {
            this.codegenoptions = codegenoptions;
#if !STATIC_COMPILER && !FIRST_PASS && !STUB_GENERATOR
            this.javaClassLoader = (java.lang.ClassLoader)javaClassLoader;
#endif
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Query if 'type' is remapped type. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="type"> The type. </param>
        ///
        /// <returns>   True if remapped type, false if not. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal static bool IsRemappedType(Type type)
        {
            return remappedTypes.ContainsKey(type);
        }

#if STATIC_COMPILER || STUB_GENERATOR
        internal void SetRemappedType(Type type, TypeWrapper tw)
        {
            lock (types)
            {
                types.Add(tw.Name, tw);
            }
            lock (globalTypeToTypeWrapper)
            {
                globalTypeToTypeWrapper.Add(type, tw);
            }
            remappedTypes.Add(type, tw.Name);
        }
#endif

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>
        /// return the TypeWrapper if it is already loaded, this exists for
        /// DynamicTypeWrapper.SetupGhosts and implements ClassLoader.findLoadedClass()
        /// </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="name"> The name. </param>
        ///
        /// <returns>   The found loaded class. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal TypeWrapper FindLoadedClass(string name)
        {
            if (name.Length > 1 && name[0] == '[')
            {
                return FindOrLoadArrayClass(name, LoadMode.Find);
            }
            TypeWrapper tw;
            lock (types)
            {
                types.TryGetValue(name, out tw);
            }
            return tw ?? FindLoadedClassLazy(name);
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Searches for the first loaded class lazy. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="name"> The name. </param>
        ///
        /// <returns>   The found loaded class lazy. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        protected virtual TypeWrapper FindLoadedClassLazy(string name)
        {
            return null;
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Registers the initiating loader described by tw. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="tw">   The tw. </param>
        ///
        /// <returns>   A TypeWrapper. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal TypeWrapper RegisterInitiatingLoader(TypeWrapper tw)
        {
            Debug.Assert(tw != null);
            Debug.Assert(!tw.IsUnloadable);
            Debug.Assert(!tw.IsPrimitive);

            try
            {
                // critical code in the finally block to avoid Thread.Abort interrupting the thread
            }
            finally
            {
                tw = RegisterInitiatingLoaderCritical(tw);
            }
            return tw;
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Registers the initiating loader critical described by tw. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="tw">   The tw. </param>
        ///
        /// <returns>   A TypeWrapper. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        private TypeWrapper RegisterInitiatingLoaderCritical(TypeWrapper tw)
        {
            lock (types)
            {
                TypeWrapper existing;
                types.TryGetValue(tw.Name, out existing);
                if (existing != tw)
                {
                    if (existing != null)
                    {
                        // another thread beat us to it, discard the new TypeWrapper and
                        // return the previous one
                        return existing;
                    }
                    // NOTE if types.ContainsKey(tw.Name) is true (i.e. the value is null),
                    // we currently have a DefineClass in progress on another thread and we've
                    // beaten that thread to the punch by loading the class from a parent class
                    // loader instead. This is ok as DefineClass will throw a LinkageError when
                    // it is done.
                    types[tw.Name] = tw;
                }
            }
            return tw;
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Gets a value indicating whether the emit debug information. </summary>
        ///
        /// <value> True if emit debug information, false if not. </value>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal bool EmitDebugInfo
        {
            get
            {
                return (codegenoptions & CodeGenOptions.Debug) != 0;
            }
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Gets a value indicating whether the emit stack trace information. </summary>
        ///
        /// <value> True if emit stack trace information, false if not. </value>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal bool EmitStackTraceInfo
        {
            get
            {
                // NOTE we're negating the flag here!
                return (codegenoptions & CodeGenOptions.NoStackTraceInfo) == 0;
            }
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Gets a value indicating whether the strict final field semantics. </summary>
        ///
        /// <value> True if strict final field semantics, false if not. </value>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal bool StrictFinalFieldSemantics
        {
            get
            {
                return (codegenoptions & CodeGenOptions.StrictFinalFieldSemantics) != 0;
            }
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Gets a value indicating whether the no jni. </summary>
        ///
        /// <value> True if no jni, false if not. </value>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal bool NoJNI
        {
            get
            {
                return (codegenoptions & CodeGenOptions.NoJNI) != 0;
            }
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Gets a value indicating whether the remove asserts. </summary>
        ///
        /// <value> True if remove asserts, false if not. </value>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal bool RemoveAsserts
        {
            get
            {
                return (codegenoptions & CodeGenOptions.RemoveAsserts) != 0;
            }
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Gets a value indicating whether the no automagic serialization. </summary>
        ///
        /// <value> True if no automagic serialization, false if not. </value>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal bool NoAutomagicSerialization
        {
            get
            {
                return (codegenoptions & CodeGenOptions.NoAutomagicSerialization) != 0;
            }
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Gets a value indicating whether the dynamic binding is disabled. </summary>
        ///
        /// <value> True if disable dynamic binding, false if not. </value>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal bool DisableDynamicBinding
        {
            get
            {
                return (codegenoptions & CodeGenOptions.DisableDynamicBinding) != 0;
            }
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Gets a value indicating whether the emit no reference emit helpers. </summary>
        ///
        /// <value> True if emit no reference emit helpers, false if not. </value>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal bool EmitNoRefEmitHelpers
        {
            get
            {
                return (codegenoptions & CodeGenOptions.NoRefEmitHelpers) != 0;
            }
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Gets a value indicating whether the remove unused fields. </summary>
        ///
        /// <value> True if remove unused fields, false if not. </value>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal bool RemoveUnusedFields
        {
            get
            {
                return (codegenoptions & CodeGenOptions.RemoveUnusedFields) != 0;
            }
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>
        /// Gets a value indicating whether the workaround abstract method widening.
        /// </summary>
        ///
        /// <value> True if workaround abstract method widening, false if not. </value>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal bool WorkaroundAbstractMethodWidening
        {
            get
            {
                // pre-Roslyn C# compiler doesn't like widening access to abstract methods
                return true;
            }
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Gets a value indicating whether the workaround interface fields. </summary>
        ///
        /// <value> True if workaround interface fields, false if not. </value>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal bool WorkaroundInterfaceFields
        {
            get
            {
                // pre-Roslyn C# compiler doesn't allow access to interface fields
                return true;
            }
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>
        /// Gets a value indicating whether the workaround interface private methods.
        /// </summary>
        ///
        /// <value> True if workaround interface private methods, false if not. </value>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal bool WorkaroundInterfacePrivateMethods
        {
            get
            {
                // pre-Roslyn C# compiler doesn't like interfaces that have non-public methods
                return true;
            }
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>
        /// Gets a value indicating whether the workaround interface static methods.
        /// </summary>
        ///
        /// <value> True if workaround interface static methods, false if not. </value>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal bool WorkaroundInterfaceStaticMethods
        {
            get
            {
                // pre-Roslyn C# compiler doesn't allow access to interface static methods
                return true;
            }
        }

#if !STATIC_COMPILER && !STUB_GENERATOR


        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Gets a value indicating whether the relaxed class name validation. </summary>
        ///
        /// <value> True if relaxed class name validation, false if not. </value>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal bool RelaxedClassNameValidation
        {
            get
            {
#if FIRST_PASS
				return true;
#else
                return JVM.relaxedVerification && (javaClassLoader == null || java.lang.ClassLoader.isTrustedLoader(javaClassLoader));
#endif
            }
        }
#endif // !STATIC_COMPILER && !STUB_GENERATOR

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Check prohibited package. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <exception cref="JavaSecurityException">    Thrown when a Java Security error condition
        ///                                             occurs. </exception>
        ///
        /// <param name="className">    Name of the class. </param>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        protected virtual void CheckProhibitedPackage(string className)
        {
            if (className.StartsWith("java.", StringComparison.Ordinal))
            {
                throw new JavaSecurityException("Prohibited package name: " + className.Substring(0, className.LastIndexOf('.')));
            }
        }

#if !STUB_GENERATOR


        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Define class. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <exception cref="NoClassDefFoundError"> Raised when a No Class Definition Found error
        ///                                         condition occurs. </exception>
        /// <exception cref="LinkageError">         Raised when a Linkage error condition occurs. </exception>
        ///
        /// <param name="f">                A ClassFile to process. </param>
        /// <param name="protectionDomain"> The protection domain. </param>
        ///
        /// <returns>   A TypeWrapper. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal TypeWrapper DefineClass(ClassFile f, ProtectionDomain protectionDomain)
        {
#if !STATIC_COMPILER
            string dotnetAssembly = f.IKVMAssemblyAttribute;
            if (dotnetAssembly != null)
            {
                // It's a stub class generated by ikvmstub (or generated by the runtime when getResource was
                // called on a statically compiled class).
                ClassLoaderWrapper loader;
                try
                {
                    loader = ClassLoaderWrapper.GetAssemblyClassLoaderByName(dotnetAssembly);
                }
                catch (Exception x)
                {
                    // TODO don't catch all exceptions here
                    throw new NoClassDefFoundError(f.Name + " (" + x.Message + ")");
                }
                TypeWrapper tw = loader.LoadClassByDottedNameFast(f.Name);
                if (tw == null)
                {
                    throw new NoClassDefFoundError(f.Name + " (type not found in " + dotnetAssembly + ")");
                }
                return RegisterInitiatingLoader(tw);
            }
#endif
            CheckProhibitedPackage(f.Name);
            // check if the class already exists if we're an AssemblyClassLoader
            if (FindLoadedClassLazy(f.Name) != null)
            {
                throw new LinkageError("duplicate class definition: " + f.Name);
            }
            TypeWrapper def;
            try
            {
                // critical code in the finally block to avoid Thread.Abort interrupting the thread
            }
            finally
            {
                def = DefineClassCritical(f, protectionDomain);
            }
            return def;
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Define class critical. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <exception cref="LinkageError"> Raised when a Linkage error condition occurs. </exception>
        ///
        /// <param name="f">                A ClassFile to process. </param>
        /// <param name="protectionDomain"> The protection domain. </param>
        ///
        /// <returns>   A TypeWrapper. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        private TypeWrapper DefineClassCritical(ClassFile f, ProtectionDomain protectionDomain)
        {
            lock (types)
            {
                if (types.ContainsKey(f.Name))
                {
                    throw new LinkageError("duplicate class definition: " + f.Name);
                }
                // mark the type as "loading in progress", so that we can detect circular dependencies.
                types.Add(f.Name, null);
                defineClassInProgress.Add(f.Name, Thread.CurrentThread);
            }
            try
            {
                return GetTypeWrapperFactory().DefineClassImpl(types, null, f, this, protectionDomain);
            }
            finally
            {
                lock (types)
                {
                    if (types[f.Name] == null)
                    {
                        // if loading the class fails, we remove the indicator that we're busy loading the class,
                        // because otherwise we get a ClassCircularityError if we try to load the class again.
                        types.Remove(f.Name);
                    }
                    defineClassInProgress.Remove(f.Name);
                    Monitor.PulseAll(types);
                }
            }
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Gets type wrapper factory. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <returns>   The type wrapper factory. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal TypeWrapperFactory GetTypeWrapperFactory()
        {
            if (factory == null)
            {
                lock (this)
                {
                    try
                    {
                        // critical code in the finally block to avoid Thread.Abort interrupting the thread
                    }
                    finally
                    {
                        if (factory == null)
                        {
#if CLASSGC
							if(dynamicAssemblies == null)
							{
								Interlocked.CompareExchange(ref dynamicAssemblies, new ConditionalWeakTable<Assembly, ClassLoaderWrapper>(), null);
							}
							typeToTypeWrapper = new Dictionary<Type, TypeWrapper>();
							DynamicClassLoader instance = DynamicClassLoader.Get(this);
							dynamicAssemblies.Add(instance.ModuleBuilder.Assembly.ManifestModule.Assembly, this);
							this.factory = instance;
#else
                            factory = DynamicClassLoader.Get(this);
#endif
                        }
                    }
                }
            }
            return factory;
        }
#endif // !STUB_GENERATOR

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Loads class by dotted name. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="name"> The name. </param>
        ///
        /// <returns>   The class by dotted name. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal TypeWrapper LoadClassByDottedName(string name)
        {
            return LoadClass(name, LoadMode.LoadOrThrow);
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Loads class by dotted name fast. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="name"> The name. </param>
        ///
        /// <returns>   The class by dotted name fast. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal TypeWrapper LoadClassByDottedNameFast(string name)
        {
            return LoadClass(name, LoadMode.LoadOrNull);
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Loads the class. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <exception cref="ClassNotFoundException">       Thrown when the Class Not Found error
        ///                                                 condition occurs. </exception>
        /// <exception cref="InvalidOperationException">    Thrown when the requested operation is
        ///                                                 invalid. </exception>
        ///
        /// <param name="name"> The name. </param>
        /// <param name="mode"> The mode. </param>
        ///
        /// <returns>   The class. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal TypeWrapper LoadClass(string name, LoadMode mode)
        {
            Profiler.Enter("LoadClass");
            try
            {
                TypeWrapper tw = LoadRegisteredOrPendingClass(name);
                if (tw != null)
                {
                    return tw;
                }
                if (name.Length > 1 && name[0] == '[')
                {
                    tw = FindOrLoadArrayClass(name, mode);
                }
                else
                {
                    tw = LoadClassImpl(name, mode);
                }
                if (tw != null)
                {
                    return RegisterInitiatingLoader(tw);
                }
#if STATIC_COMPILER
				if (!(name.Length > 1 && name[0] == '[') && ((mode & LoadMode.WarnClassNotFound) != 0) || WarningLevelHigh)
				{
					IssueMessage(Message.ClassNotFound, name);
				}
#else
                if (!(name.Length > 1 && name[0] == '['))
                {
                    Tracer.Error(Tracer.ClassLoading, "Class not found: {0}", name);
                }
#endif
                switch (mode & LoadMode.MaskReturn)
                {
                    case LoadMode.ReturnNull:
                        return null;
                    case LoadMode.ReturnUnloadable:
                        return new UnloadableTypeWrapper(name);
                    case LoadMode.ThrowClassNotFound:
                        throw new ClassNotFoundException(name);
                    default:
                        throw new InvalidOperationException();
                }
            }
            finally
            {
                Profiler.Leave("LoadClass");
            }
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Loads registered or pending class. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <exception cref="ClassCircularityError">    Raised when the Class Circularity error condition
        ///                                             occurs. </exception>
        ///
        /// <param name="name"> The name. </param>
        ///
        /// <returns>   The registered or pending class. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        private TypeWrapper LoadRegisteredOrPendingClass(string name)
        {
            TypeWrapper tw;
            lock (types)
            {
                if (types.TryGetValue(name, out tw) && tw == null)
                {
                    Thread defineThread;
                    if (defineClassInProgress.TryGetValue(name, out defineThread))
                    {
                        if (Thread.CurrentThread == defineThread)
                        {
                            throw new ClassCircularityError(name);
                        }
                        // the requested class is currently being defined by another thread,
                        // so we have to wait on that
                        while (defineClassInProgress.ContainsKey(name))
                        {
                            Monitor.Wait(types);
                        }
                        // the defineClass may have failed, so we need to use TryGetValue
                        types.TryGetValue(name, out tw);
                    }
                }
            }
            return tw;
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Searches for the first or load array class. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="name"> The name. </param>
        /// <param name="mode"> The mode. </param>
        ///
        /// <returns>   The found or load array class. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        private TypeWrapper FindOrLoadArrayClass(string name, LoadMode mode)
        {
            int dims = 1;
            while (name[dims] == '[')
            {
                dims++;
                if (dims == name.Length)
                {
                    // malformed class name
                    return null;
                }
            }
            if (name[dims] == 'L')
            {
                if (!name.EndsWith(";") || name.Length <= dims + 2 || name[dims + 1] == '[')
                {
                    // malformed class name
                    return null;
                }
                string elemClass = name.Substring(dims + 1, name.Length - dims - 2);
                // NOTE it's important that we're registered as the initiating loader
                // for the element type here
                TypeWrapper type = LoadClass(elemClass, mode | LoadMode.DontReturnUnloadable);
                if (type != null)
                {
                    type = CreateArrayType(name, type, dims);
                }
                return type;
            }
            if (name.Length != dims + 1)
            {
                // malformed class name
                return null;
            }
            switch (name[dims])
            {
                case 'B':
                    return CreateArrayType(name, PrimitiveTypeWrapper.BYTE, dims);
                case 'C':
                    return CreateArrayType(name, PrimitiveTypeWrapper.CHAR, dims);
                case 'D':
                    return CreateArrayType(name, PrimitiveTypeWrapper.DOUBLE, dims);
                case 'F':
                    return CreateArrayType(name, PrimitiveTypeWrapper.FLOAT, dims);
                case 'I':
                    return CreateArrayType(name, PrimitiveTypeWrapper.INT, dims);
                case 'J':
                    return CreateArrayType(name, PrimitiveTypeWrapper.LONG, dims);
                case 'S':
                    return CreateArrayType(name, PrimitiveTypeWrapper.SHORT, dims);
                case 'Z':
                    return CreateArrayType(name, PrimitiveTypeWrapper.BOOLEAN, dims);
                default:
                    return null;
            }
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Searches for the first or load generic class. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="name"> The name. </param>
        /// <param name="mode"> The mode. </param>
        ///
        /// <returns>   The found or load generic class. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal TypeWrapper FindOrLoadGenericClass(string name, LoadMode mode)
        {
            // we don't want to expose any failures to load any of the component types
            mode = (mode & LoadMode.MaskReturn) | LoadMode.ReturnNull;

            // we need to handle delegate methods here (for generic delegates)
            // (note that other types with manufactured inner classes such as Attribute and Enum can't be generic)
            if (name.EndsWith(DotNetTypeWrapper.DelegateInterfaceSuffix))
            {
                TypeWrapper outer = FindOrLoadGenericClass(name.Substring(0, name.Length - DotNetTypeWrapper.DelegateInterfaceSuffix.Length), mode);
                if (outer != null && outer.IsFakeTypeContainer)
                {
                    foreach (TypeWrapper tw in outer.InnerClasses)
                    {
                        if (tw.Name == name)
                        {
                            return tw;
                        }
                    }
                }
            }
            // generic class name grammar:
            //
            // mangled(open_generic_type_name) "_$$$_" M(parameter_class_name) ( "_$$_" M(parameter_class_name) )* "_$$$$_"
            //
            // mangled() is the normal name mangling algorithm
            // M() is a replacement of "__" with "$$005F$$005F" followed by a replace of "." with "__"
            //
            int pos = name.IndexOf("_$$$_");
            if (pos <= 0 || !name.EndsWith("_$$$$_"))
            {
                return null;
            }
            TypeWrapper def = LoadClass(name.Substring(0, pos), mode);
            if (def == null || !def.TypeAsTBD.IsGenericTypeDefinition)
            {
                return null;
            }
            Type type = def.TypeAsTBD;
            List<string> typeParamNames = new List<string>();
            pos += 5;
            int start = pos;
            int nest = 0;
            for (; ; )
            {
                pos = name.IndexOf("_$$", pos);
                if (pos == -1)
                {
                    return null;
                }
                if (name.IndexOf("_$$_", pos, 4) == pos)
                {
                    if (nest == 0)
                    {
                        typeParamNames.Add(name.Substring(start, pos - start));
                        start = pos + 4;
                    }
                    pos += 4;
                }
                else if (name.IndexOf("_$$$_", pos, 5) == pos)
                {
                    nest++;
                    pos += 5;
                }
                else if (name.IndexOf("_$$$$_", pos, 6) == pos)
                {
                    if (nest == 0)
                    {
                        if (pos + 6 != name.Length)
                        {
                            return null;
                        }
                        typeParamNames.Add(name.Substring(start, pos - start));
                        break;
                    }
                    nest--;
                    pos += 6;
                }
                else
                {
                    pos += 3;
                }
            }
            Type[] typeArguments = new Type[typeParamNames.Count];
            for (int i = 0; i < typeArguments.Length; i++)
            {
                string s = (string)typeParamNames[i];
                // only do the unmangling for non-generic types (because we don't want to convert
                // the double underscores in two adjacent _$$$_ or _$$$$_ markers)
                if (s.IndexOf("_$$$_") == -1)
                {
                    s = s.Replace("__", ".");
                    s = s.Replace("$$005F$$005F", "__");
                }
                int dims = 0;
                while (s.Length > dims && s[dims] == 'A')
                {
                    dims++;
                }
                if (s.Length == dims)
                {
                    return null;
                }
                TypeWrapper tw;
                switch (s[dims])
                {
                    case 'L':
                        tw = LoadClass(s.Substring(dims + 1), mode);
                        if (tw == null)
                        {
                            return null;
                        }
                        tw.Finish();
                        break;
                    case 'Z':
                        tw = PrimitiveTypeWrapper.BOOLEAN;
                        break;
                    case 'B':
                        tw = PrimitiveTypeWrapper.BYTE;
                        break;
                    case 'S':
                        tw = PrimitiveTypeWrapper.SHORT;
                        break;
                    case 'C':
                        tw = PrimitiveTypeWrapper.CHAR;
                        break;
                    case 'I':
                        tw = PrimitiveTypeWrapper.INT;
                        break;
                    case 'F':
                        tw = PrimitiveTypeWrapper.FLOAT;
                        break;
                    case 'J':
                        tw = PrimitiveTypeWrapper.LONG;
                        break;
                    case 'D':
                        tw = PrimitiveTypeWrapper.DOUBLE;
                        break;
                    default:
                        return null;
                }
                if (dims > 0)
                {
                    tw = tw.MakeArrayType(dims);
                }
                typeArguments[i] = tw.TypeAsSignatureType;
            }
            try
            {
                type = type.MakeGenericType(typeArguments);
            }
            catch (ArgumentException)
            {
                // one of the typeArguments failed to meet the constraints
                return null;
            }
            TypeWrapper wrapper = GetWrapperFromType(type);
            if (wrapper != null && wrapper.Name != name)
            {
                // the name specified was not in canonical form
                return null;
            }
            return wrapper;
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Loads class implementation. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <exception cref="ClassLoadingException">    Thrown when the Class Loading error condition
        ///                                             occurs. </exception>
        /// <exception cref="ThreadDeath">              Thrown when a thread death error condition
        ///                                             occurs. </exception>
        ///
        /// <param name="name"> The name. </param>
        /// <param name="mode"> The mode. </param>
        ///
        /// <returns>   The class implementation. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        protected virtual TypeWrapper LoadClassImpl(string name, LoadMode mode)
        {
            TypeWrapper tw = FindOrLoadGenericClass(name, mode);
            if (tw != null)
            {
                return tw;
            }
#if !STATIC_COMPILER && !FIRST_PASS && !STUB_GENERATOR
            if ((mode & LoadMode.Load) == 0)
            {
                return null;
            }
            Profiler.Enter("ClassLoader.loadClass");
            try
            {
                java.lang.Class c = GetJavaClassLoader().loadClassInternal(name);
                if (c == null)
                {
                    return null;
                }
                TypeWrapper type = TypeWrapper.FromClass(c);
                if (type.Name != name)
                {
                    // the class loader is trying to trick us
                    return null;
                }
                return type;
            }
            catch (java.lang.ClassNotFoundException x)
            {
                if ((mode & LoadMode.MaskReturn) == LoadMode.ThrowClassNotFound)
                {
                    throw new ClassLoadingException(ikvm.runtime.Util.mapException(x), name);
                }
                return null;
            }
            catch (java.lang.ThreadDeath)
            {
                throw;
            }
            catch (Exception x)
            {
                if ((mode & LoadMode.SuppressExceptions) == 0)
                {
                    throw new ClassLoadingException(ikvm.runtime.Util.mapException(x), name);
                }
                if (Tracer.ClassLoading.TraceError)
                {
                    java.lang.ClassLoader cl = GetJavaClassLoader();
                    if (cl != null)
                    {
                        System.Text.StringBuilder sb = new System.Text.StringBuilder();
                        string sep = "";
                        while (cl != null)
                        {
                            sb.Append(sep).Append(cl);
                            sep = " -> ";
                            cl = cl.getParent();
                        }
                        Tracer.Error(Tracer.ClassLoading, "ClassLoader chain: {0}", sb);
                    }
                    Exception m = ikvm.runtime.Util.mapException(x);
                    Tracer.Error(Tracer.ClassLoading, m.ToString() + Environment.NewLine + m.StackTrace);
                }
                return null;
            }
            finally
            {
                Profiler.Leave("ClassLoader.loadClass");
            }
#else
            return null;
#endif
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Creates array type. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="name">                 The name. </param>
        /// <param name="elementTypeWrapper">   The element type wrapper. </param>
        /// <param name="dims">                 The dims. </param>
        ///
        /// <returns>   The new array type. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        private static TypeWrapper CreateArrayType(string name, TypeWrapper elementTypeWrapper, int dims)
        {
            Debug.Assert(new String('[', dims) + elementTypeWrapper.SigName == name);
            Debug.Assert(!elementTypeWrapper.IsUnloadable && !elementTypeWrapper.IsVerifierType && !elementTypeWrapper.IsArray);
            Debug.Assert(dims >= 1);
            return elementTypeWrapper.GetClassLoader().RegisterInitiatingLoader(new ArrayTypeWrapper(elementTypeWrapper, name));
        }

#if !STATIC_COMPILER && !STUB_GENERATOR


        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Gets java class loader. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <returns>   The java class loader. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal virtual java.lang.ClassLoader GetJavaClassLoader()
        {
#if FIRST_PASS
			return null;
#else
            return javaClassLoader;
#endif
        }
#endif

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   NOTE this exposes potentially unfinished types. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="sig">  The signal. </param>
        ///
        /// <returns>   A Type[]. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal Type[] ArgTypeListFromSig(string sig)
        {
            if (sig[1] == ')')
            {
                return Type.EmptyTypes;
            }
            TypeWrapper[] wrappers = ArgTypeWrapperListFromSig(sig, LoadMode.LoadOrThrow);
            Type[] types = new Type[wrappers.Length];
            for (int i = 0; i < wrappers.Length; i++)
            {
                types[i] = wrappers[i].TypeAsSignatureType;
            }
            return types;
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>
        /// NOTE: this will ignore anything following the sig marker (so that it can be used to decode
        /// method signatures)
        /// </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <exception cref="InvalidOperationException">    Thrown when the requested operation is
        ///                                                 invalid. </exception>
        ///
        /// <param name="index">    [in,out] Zero-based index of the. </param>
        /// <param name="sig">      The signal. </param>
        /// <param name="mode">     The mode. </param>
        ///
        /// <returns>   A TypeWrapper. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        private TypeWrapper SigDecoderWrapper(ref int index, string sig, LoadMode mode)
        {
            switch (sig[index++])
            {
                case 'B':
                    return PrimitiveTypeWrapper.BYTE;
                case 'C':
                    return PrimitiveTypeWrapper.CHAR;
                case 'D':
                    return PrimitiveTypeWrapper.DOUBLE;
                case 'F':
                    return PrimitiveTypeWrapper.FLOAT;
                case 'I':
                    return PrimitiveTypeWrapper.INT;
                case 'J':
                    return PrimitiveTypeWrapper.LONG;
                case 'L':
                    {
                        int pos = index;
                        index = sig.IndexOf(';', index) + 1;
                        return LoadClass(sig.Substring(pos, index - pos - 1), mode);
                    }
                case 'S':
                    return PrimitiveTypeWrapper.SHORT;
                case 'Z':
                    return PrimitiveTypeWrapper.BOOLEAN;
                case 'V':
                    return PrimitiveTypeWrapper.VOID;
                case '[':
                    {
                        // TODO this can be optimized
                        string array = "[";
                        while (sig[index] == '[')
                        {
                            index++;
                            array += "[";
                        }
                        switch (sig[index])
                        {
                            case 'L':
                                {
                                    int pos = index;
                                    index = sig.IndexOf(';', index) + 1;
                                    return LoadClass(array + sig.Substring(pos, index - pos), mode);
                                }
                            case 'B':
                            case 'C':
                            case 'D':
                            case 'F':
                            case 'I':
                            case 'J':
                            case 'S':
                            case 'Z':
                                return LoadClass(array + sig[index++], mode);
                            default:
                                throw new InvalidOperationException(sig.Substring(index));
                        }
                    }
                default:
                    throw new InvalidOperationException(sig.Substring(index));
            }
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Field type wrapper from signal. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="sig">  The signal. </param>
        /// <param name="mode"> The mode. </param>
        ///
        /// <returns>   A TypeWrapper. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal TypeWrapper FieldTypeWrapperFromSig(string sig, LoadMode mode)
        {
            int index = 0;
            return SigDecoderWrapper(ref index, sig, mode);
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Ret type wrapper from signal. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="sig">  The signal. </param>
        /// <param name="mode"> The mode. </param>
        ///
        /// <returns>   A TypeWrapper. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal TypeWrapper RetTypeWrapperFromSig(string sig, LoadMode mode)
        {
            int index = sig.IndexOf(')') + 1;
            return SigDecoderWrapper(ref index, sig, mode);
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Argument type wrapper list from signal. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="sig">  The signal. </param>
        /// <param name="mode"> The mode. </param>
        ///
        /// <returns>   A TypeWrapper[]. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal TypeWrapper[] ArgTypeWrapperListFromSig(string sig, LoadMode mode)
        {
            if (sig[1] == ')')
            {
                return TypeWrapper.EmptyArray;
            }
            List<TypeWrapper> list = new List<TypeWrapper>();
            for (int i = 1; sig[i] != ')';)
            {
                list.Add(SigDecoderWrapper(ref i, sig, mode));
            }
            return list.ToArray();
        }

#if STATIC_COMPILER || STUB_GENERATOR
        internal static ClassLoaderWrapper GetBootstrapClassLoader()
#else


        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Gets the endif. </summary>
        ///
        /// <value> The endif. </value>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal static AssemblyClassLoader GetBootstrapClassLoader()
#endif
        {
            lock (wrapperLock)
            {
                if (bootstrapClassLoader == null)
                {
                    bootstrapClassLoader = new BootstrapClassLoader();
                }
                return bootstrapClassLoader;
            }
        }

#if !STATIC_COMPILER && !STUB_GENERATOR


        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Gets class loader wrapper. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="javaClassLoader">  The java class loader. </param>
        ///
        /// <returns>   The class loader wrapper. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal static ClassLoaderWrapper GetClassLoaderWrapper(java.lang.ClassLoader javaClassLoader)
        {
            if (javaClassLoader == null)
            {
                return GetBootstrapClassLoader();
            }
            lock (wrapperLock)
            {
#if FIRST_PASS
				ClassLoaderWrapper wrapper = null;
#else
                ClassLoaderWrapper wrapper =
#if __MonoCS__
					// MONOBUG the redundant cast to ClassLoaderWrapper is to workaround an mcs bug
					(ClassLoaderWrapper)(object)
#endif
                    javaClassLoader.wrapper;
#endif
                if (wrapper == null)
                {
                    CodeGenOptions opt = CodeGenOptions.None;
                    if (JVM.EmitSymbols)
                    {
                        opt |= CodeGenOptions.Debug;
                    }
#if NET_4_0
					if (!AppDomain.CurrentDomain.IsFullyTrusted)
					{
						opt |= CodeGenOptions.NoAutomagicSerialization;
					}
#endif
                    wrapper = new ClassLoaderWrapper(opt, javaClassLoader);
                    SetWrapperForClassLoader(javaClassLoader, wrapper);
                }
                return wrapper;
            }
        }
#endif

#if CLASSGC
		internal static ClassLoaderWrapper GetClassLoaderForDynamicJavaAssembly(Assembly asm)
		{
			ClassLoaderWrapper loader;
			dynamicAssemblies.TryGetValue(asm, out loader);
			return loader;
		}
#endif // CLASSGC

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Gets wrapper from type. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="type"> The type. </param>
        ///
        /// <returns>   The wrapper from type. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal static TypeWrapper GetWrapperFromType(Type type)
        {
#if STATIC_COMPILER
			if (type.__ContainsMissingType)
			{
				return new UnloadableTypeWrapper(type);
			}
#endif
            //Tracer.Info(Tracer.Runtime, "GetWrapperFromType: {0}", type.AssemblyQualifiedName);
#if !STATIC_COMPILER
            TypeWrapper.AssertFinished(type);
#endif
            Debug.Assert(!type.IsPointer);
            Debug.Assert(!type.IsByRef);
            TypeWrapper wrapper;
            lock (globalTypeToTypeWrapper)
            {
                globalTypeToTypeWrapper.TryGetValue(type, out wrapper);
            }
            if (wrapper != null)
            {
                return wrapper;
            }
#if STUB_GENERATOR
            if (type.__IsMissing || type.__ContainsMissingType)
            {
                wrapper = new UnloadableTypeWrapper("Missing/" + type.Assembly.FullName);
                globalTypeToTypeWrapper.Add(type, wrapper);
                return wrapper;
            }
#endif
            string remapped;
            if (remappedTypes.TryGetValue(type, out remapped))
            {
                wrapper = LoadClassCritical(remapped);
            }
            else if (ReflectUtil.IsVector(type))
            {
                // it might be an array of a dynamically compiled Java type
                int rank = 1;
                Type elem = type.GetElementType();
                while (ReflectUtil.IsVector(elem))
                {
                    rank++;
                    elem = elem.GetElementType();
                }
                wrapper = GetWrapperFromType(elem).MakeArrayType(rank);
            }
            else
            {
                Assembly asm = type.Assembly;
#if CLASSGC
				ClassLoaderWrapper loader = null;
				if(dynamicAssemblies != null && dynamicAssemblies.TryGetValue(asm, out loader))
				{
					lock(loader.typeToTypeWrapper)
					{
						TypeWrapper tw;
						if(loader.typeToTypeWrapper.TryGetValue(type, out tw))
						{
							return tw;
						}
						// it must be an anonymous type then
						Debug.Assert(AnonymousTypeWrapper.IsAnonymous(type));
					}
				}
#endif
#if !STATIC_COMPILER && !STUB_GENERATOR
                if (AnonymousTypeWrapper.IsAnonymous(type))
                {
                    Dictionary<Type, TypeWrapper> typeToTypeWrapper;
#if CLASSGC
					typeToTypeWrapper = loader != null ? loader.typeToTypeWrapper : globalTypeToTypeWrapper;
#else
                    typeToTypeWrapper = globalTypeToTypeWrapper;
#endif
                    TypeWrapper tw = new AnonymousTypeWrapper(type);
                    lock (typeToTypeWrapper)
                    {
                        if (!typeToTypeWrapper.TryGetValue(type, out wrapper))
                        {
                            typeToTypeWrapper.Add(type, wrapper = tw);
                        }
                    }
                    return wrapper;
                }
                if (ReflectUtil.IsReflectionOnly(type))
                {
                    // historically we've always returned null for types that don't have a corresponding TypeWrapper (or java.lang.Class)
                    return null;
                }
#endif
                // if the wrapper doesn't already exist, that must mean that the type
                // is a .NET type (or a pre-compiled Java class), which means that it
                // was "loaded" by an assembly classloader
                wrapper = AssemblyClassLoader.FromAssembly(asm).GetWrapperFromAssemblyType(type);
            }
#if CLASSGC
			if(type.Assembly.IsDynamic)
			{
				// don't cache types in dynamic assemblies, because they might live in a RunAndCollect assembly
				// TODO we also shouldn't cache generic type instances that have a GCable type parameter
				return wrapper;
			}
#endif
            lock (globalTypeToTypeWrapper)
            {
                try
                {
                    // critical code in the finally block to avoid Thread.Abort interrupting the thread
                }
                finally
                {
                    globalTypeToTypeWrapper[type] = wrapper;
                }
            }
            return wrapper;
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Gets generic class loader. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="wrapper">  The wrapper. </param>
        ///
        /// <returns>   The generic class loader. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal static ClassLoaderWrapper GetGenericClassLoader(TypeWrapper wrapper)
        {
            Type type = wrapper.TypeAsTBD;
            Debug.Assert(type.IsGenericType);
            Debug.Assert(!type.ContainsGenericParameters);

            List<ClassLoaderWrapper> list = new List<ClassLoaderWrapper>();
            list.Add(AssemblyClassLoader.FromAssembly(type.Assembly));
            foreach (Type arg in type.GetGenericArguments())
            {
                ClassLoaderWrapper loader = GetWrapperFromType(arg).GetClassLoader();
                if (!list.Contains(loader) && loader != bootstrapClassLoader)
                {
                    list.Add(loader);
                }
            }
            ClassLoaderWrapper[] key = list.ToArray();
            ClassLoaderWrapper matchingLoader = GetGenericClassLoaderByKey(key);
            matchingLoader.RegisterInitiatingLoader(wrapper);
            return matchingLoader;
        }

#if !STATIC_COMPILER && !FIRST_PASS && !STUB_GENERATOR


        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Executes the privileged operation. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="action">   The action. </param>
        ///
        /// <returns>   An object. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal static object DoPrivileged(java.security.PrivilegedAction action)
        {
            return java.security.AccessController.doPrivileged(action, ikvm.@internal.CallerID.create(typeof(java.lang.ClassLoader).TypeHandle));
        }
#endif

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Gets generic class loader by key. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="key">  The key. </param>
        ///
        /// <returns>   The generic class loader by key. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        private static ClassLoaderWrapper GetGenericClassLoaderByKey(ClassLoaderWrapper[] key)
        {
            lock (wrapperLock)
            {
                if (genericClassLoaders == null)
                {
                    genericClassLoaders = new List<GenericClassLoaderWrapper>();
                }
                foreach (GenericClassLoaderWrapper loader in genericClassLoaders)
                {
                    if (loader.Matches(key))
                    {
                        return loader;
                    }
                }
#if STATIC_COMPILER || STUB_GENERATOR || FIRST_PASS
                GenericClassLoaderWrapper newLoader = new GenericClassLoaderWrapper(key, null);
#else
                java.lang.ClassLoader javaClassLoader = new ikvm.runtime.GenericClassLoader();
                GenericClassLoaderWrapper newLoader = new GenericClassLoaderWrapper(key, javaClassLoader);
                SetWrapperForClassLoader(javaClassLoader, newLoader);
#endif
                genericClassLoaders.Add(newLoader);
                return newLoader;
            }
        }

#if !STATIC_COMPILER && !STUB_GENERATOR


        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Sets wrapper for class loader. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="javaClassLoader">  The java class loader. </param>
        /// <param name="wrapper">          The wrapper. </param>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        protected internal static void SetWrapperForClassLoader(java.lang.ClassLoader javaClassLoader, ClassLoaderWrapper wrapper)
        {
#if __MonoCS__ || FIRST_PASS
			typeof(java.lang.ClassLoader).GetField("wrapper", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(javaClassLoader, wrapper);
#else
            javaClassLoader.wrapper = wrapper;
#endif
        }
#endif

#if !STATIC_COMPILER && !STUB_GENERATOR


        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Gets generic class loader by name. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <exception cref="InvalidOperationException">    Thrown when the requested operation is
        ///                                                 invalid. </exception>
        ///
        /// <param name="name"> The name. </param>
        ///
        /// <returns>   The generic class loader by name. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal static ClassLoaderWrapper GetGenericClassLoaderByName(string name)
        {
            Debug.Assert(name.StartsWith("[[") && name.EndsWith("]]"));
            Stack<List<ClassLoaderWrapper>> stack = new Stack<List<ClassLoaderWrapper>>();
            List<ClassLoaderWrapper> list = null;
            for (int i = 0; i < name.Length; i++)
            {
                if (name[i] == '[')
                {
                    if (name[i + 1] == '[')
                    {
                        stack.Push(list);
                        list = new List<ClassLoaderWrapper>();
                        if (name[i + 2] == '[')
                        {
                            i++;
                        }
                    }
                    else
                    {
                        int start = i + 1;
                        i = name.IndexOf(']', i);
                        list.Add(ClassLoaderWrapper.GetAssemblyClassLoaderByName(name.Substring(start, i - start)));
                    }
                }
                else if (name[i] == ']')
                {
                    ClassLoaderWrapper loader = GetGenericClassLoaderByKey(list.ToArray());
                    list = stack.Pop();
                    if (list == null)
                    {
                        return loader;
                    }
                    list.Add(loader);
                }
                else
                {
                    throw new InvalidOperationException();
                }
            }
            throw new InvalidOperationException();
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Gets assembly class loader by name. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="name"> The name. </param>
        ///
        /// <returns>   The assembly class loader by name. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal static ClassLoaderWrapper GetAssemblyClassLoaderByName(string name)
        {
            if (name.StartsWith("[["))
            {
                return GetGenericClassLoaderByName(name);
            }
            return AssemblyClassLoader.FromAssembly(Assembly.Load(name));
        }
#endif

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Gets generic class loader identifier. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="wrapper">  The wrapper. </param>
        ///
        /// <returns>   The generic class loader identifier. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal static int GetGenericClassLoaderId(ClassLoaderWrapper wrapper)
        {
            lock (wrapperLock)
            {
                return genericClassLoaders.IndexOf(wrapper as GenericClassLoaderWrapper);
            }
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Gets generic class loader by identifier. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="id">   The identifier. </param>
        ///
        /// <returns>   The generic class loader by identifier. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal static ClassLoaderWrapper GetGenericClassLoaderById(int id)
        {
            lock (wrapperLock)
            {
                return genericClassLoaders[id];
            }
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Sets wrapper for type. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="type">     The type. </param>
        /// <param name="wrapper">  The wrapper. </param>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal void SetWrapperForType(Type type, TypeWrapper wrapper)
        {
#if !STATIC_COMPILER
            TypeWrapper.AssertFinished(type);
#endif
            Dictionary<Type, TypeWrapper> dict;
#if CLASSGC
			dict = typeToTypeWrapper ?? globalTypeToTypeWrapper;
#else
            dict = globalTypeToTypeWrapper;
#endif
            lock (dict)
            {
                try
                {
                    // critical code in the finally block to avoid Thread.Abort interrupting the thread
                }
                finally
                {
                    dict.Add(type, wrapper);
                }
            }
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Loads class critical. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <exception cref="FatalCompilerErrorException">  Thrown when a Fatal Compiler Error error
        ///                                                 condition occurs. </exception>
        ///
        /// <param name="name"> The name. </param>
        ///
        /// <returns>   The class critical. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal static TypeWrapper LoadClassCritical(string name)
        {
#if STATIC_COMPILER
			TypeWrapper wrapper = GetBootstrapClassLoader().LoadClassByDottedNameFast(name);
			if (wrapper == null)
			{
				throw new FatalCompilerErrorException(Message.CriticalClassNotFound, name);
			}
			return wrapper;
#else
            try
            {
                return GetBootstrapClassLoader().LoadClassByDottedName(name);
            }
            catch (Exception x)
            {
                JVM.CriticalFailure("Loading of critical class failed", x);
                return null;
            }
#endif
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Registers the native library described by p. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="p">    An IntPtr to process. </param>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal void RegisterNativeLibrary(IntPtr p)
        {
            lock (this)
            {
                try
                {
                    // critical code in the finally block to avoid Thread.Abort interrupting the thread
                }
                finally
                {
                    if (nativeLibraries == null)
                    {
                        nativeLibraries = new List<IntPtr>();
                    }
                    nativeLibraries.Add(p);
                }
            }
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Unregisters the native library described by p. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="p">    An IntPtr to process. </param>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal void UnregisterNativeLibrary(IntPtr p)
        {
            lock (this)
            {
                try
                {
                    // critical code in the finally block to avoid Thread.Abort interrupting the thread
                }
                finally
                {
                    nativeLibraries.Remove(p);
                }
            }
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Gets native libraries. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <returns>   An array of int pointer. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal IntPtr[] GetNativeLibraries()
        {
            lock (this)
            {
                if (nativeLibraries == null)
                {
                    return new IntPtr[0];
                }
                return nativeLibraries.ToArray();
            }
        }

#if !STATIC_COMPILER && !FIRST_PASS && !STUB_GENERATOR


        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Returns a string that represents the current object. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <returns>   A string that represents the current object. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        public override string ToString()
        {
            object javaClassLoader = GetJavaClassLoader();
            if (javaClassLoader == null)
            {
                return "null";
            }
            return String.Format("{0}@{1:X}", GetWrapperFromType(javaClassLoader.GetType()).Name, javaClassLoader.GetHashCode());
        }
#endif

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Internals visible to implementation. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="wrapper">  The wrapper. </param>
        /// <param name="friend">   The friend. </param>
        ///
        /// <returns>   True if it succeeds, false if it fails. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal virtual bool InternalsVisibleToImpl(TypeWrapper wrapper, TypeWrapper friend)
        {
            Debug.Assert(wrapper.GetClassLoader() == this);
            return this == friend.GetClassLoader();
        }

#if !STATIC_COMPILER && !STUB_GENERATOR


        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   this method is used by IKVM.Runtime.JNI. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="callerID"> Identifier for the caller. </param>
        ///
        /// <returns>   A ClassLoaderWrapper. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal static ClassLoaderWrapper FromCallerID(ikvm.@internal.CallerID callerID)
        {
#if FIRST_PASS
			return null;
#else
            return GetClassLoaderWrapper(callerID.getCallerClassLoader());
#endif
        }
#endif

#if STATIC_COMPILER
		internal virtual void IssueMessage(Message msgId, params string[] values)
		{
			// it's not ideal when we end up here (because it means we're emitting a warning that is not associated with a specific output target),
			// but it happens when we're decoding something in a referenced assembly that either doesn't make sense or contains an unloadable type
			StaticCompiler.IssueMessage(msgId, values);
		}
#endif

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Check package access. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="tw">   The tw. </param>
        /// <param name="pd">   The pd. </param>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal void CheckPackageAccess(TypeWrapper tw, ProtectionDomain pd)
        {
#if !STATIC_COMPILER && !FIRST_PASS && !STUB_GENERATOR
            if (javaClassLoader != null)
            {
                javaClassLoader.checkPackageAccess(tw.ClassObject, pd);
            }
#endif
        }

#if !STUB_GENERATOR


        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Gets options for controlling the class file parse. </summary>
        ///
        /// <value> Options that control the class file parse. </value>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal ClassFileParseOptions ClassFileParseOptions
        {
            get
            {
#if STATIC_COMPILER
				ClassFileParseOptions cfp = ClassFileParseOptions.LocalVariableTable;
				if (EmitStackTraceInfo)
				{
					cfp |= ClassFileParseOptions.LineNumberTable;
				}
				if (bootstrapClassLoader is CompilerClassLoader)
				{
					cfp |= ClassFileParseOptions.TrustedAnnotations;
				}
				if (RemoveAsserts)
				{
					cfp |= ClassFileParseOptions.RemoveAssertions;
				}
				return cfp;
#else
                ClassFileParseOptions cfp = ClassFileParseOptions.LineNumberTable;
                if (EmitDebugInfo)
                {
                    cfp |= ClassFileParseOptions.LocalVariableTable;
                }
                if (RelaxedClassNameValidation)
                {
                    cfp |= ClassFileParseOptions.RelaxedClassNameValidation;
                }
                if (this == bootstrapClassLoader)
                {
                    cfp |= ClassFileParseOptions.TrustedAnnotations;
                }
                return cfp;
#endif
            }
        }
#endif

#if STATIC_COMPILER
		internal virtual bool WarningLevelHigh
		{
			get { return false; }
		}

		internal virtual bool NoParameterReflection
		{
			get { return false; }
		}
#endif
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    /// <summary>   A generic class loader wrapper. This class cannot be inherited. </summary>
    ///
    /// <remarks>   Semantika d.o.o.,. </remarks>
    ////////////////////////////////////////////////////////////////////////////////////////////////////
    sealed class GenericClassLoaderWrapper : ClassLoaderWrapper
    {
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   The delegates. </summary>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        private readonly ClassLoaderWrapper[] delegates;

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Constructor. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="delegates">        The delegates. </param>
        /// <param name="javaClassLoader">  The java class loader. </param>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal GenericClassLoaderWrapper(ClassLoaderWrapper[] delegates, object javaClassLoader)
            : base(CodeGenOptions.None, javaClassLoader)
        {
            this.delegates = delegates;
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Matches the given key. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="key">  The key. </param>
        ///
        /// <returns>   True if it succeeds, false if it fails. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal bool Matches(ClassLoaderWrapper[] key)
        {
            if (key.Length == delegates.Length)
            {
                for (int i = 0; i < key.Length; i++)
                {
                    if (key[i] != delegates[i])
                    {
                        return false;
                    }
                }
                return true;
            }
            return false;
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Searches for the first loaded class lazy. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="name"> The name. </param>
        ///
        /// <returns>   The found loaded class lazy. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        protected override TypeWrapper FindLoadedClassLazy(string name)
        {
            TypeWrapper tw1 = FindOrLoadGenericClass(name, LoadMode.Find);
            if (tw1 != null)
            {
                return tw1;
            }
            foreach (ClassLoaderWrapper loader in delegates)
            {
                TypeWrapper tw = loader.FindLoadedClass(name);
                if (tw != null && tw.GetClassLoader() == loader)
                {
                    return tw;
                }
            }
            return null;
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Gets the name. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <returns>   The name. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal string GetName()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append('[');
            foreach (ClassLoaderWrapper loader in delegates)
            {
                sb.Append('[');
                GenericClassLoaderWrapper gcl = loader as GenericClassLoaderWrapper;
                if (gcl != null)
                {
                    sb.Append(gcl.GetName());
                }
                else
                {
                    sb.Append(((AssemblyClassLoader)loader).MainAssembly.FullName);
                }
                sb.Append(']');
            }
            sb.Append(']');
            return sb.ToString();
        }

#if !STATIC_COMPILER && !STUB_GENERATOR


        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Gets the resources. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="name"> The name. </param>
        ///
        /// <returns>   The resources. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal java.util.Enumeration GetResources(string name)
        {
#if FIRST_PASS
			return null;
#else
            java.util.Vector v = new java.util.Vector();
            foreach (java.net.URL url in GetBootstrapClassLoader().GetResources(name))
            {
                v.add(url);
            }
            if (name.EndsWith(".class", StringComparison.Ordinal) && name.IndexOf('.') == name.Length - 6)
            {
                TypeWrapper tw = FindLoadedClass(name.Substring(0, name.Length - 6).Replace('/', '.'));
                if (tw != null && !tw.IsArray && !tw.IsDynamic)
                {
                    ClassLoaderWrapper loader = tw.GetClassLoader();
                    if (loader is GenericClassLoaderWrapper)
                    {
                        v.add(new java.net.URL("ikvmres", "gen", ClassLoaderWrapper.GetGenericClassLoaderId(loader), "/" + name));
                    }
                    else if (loader is AssemblyClassLoader)
                    {
                        foreach (java.net.URL url in ((AssemblyClassLoader)loader).FindResources(name))
                        {
                            v.add(url);
                        }
                    }
                }
            }
            return v.elements();
#endif
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Searches for the first resource. </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="name"> The name. </param>
        ///
        /// <returns>   The found resource. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        internal java.net.URL FindResource(string name)
        {
#if !FIRST_PASS
            if (name.EndsWith(".class", StringComparison.Ordinal) && name.IndexOf('.') == name.Length - 6)
            {
                TypeWrapper tw = FindLoadedClass(name.Substring(0, name.Length - 6).Replace('/', '.'));
                if (tw != null && tw.GetClassLoader() == this && !tw.IsArray && !tw.IsDynamic)
                {
                    return new java.net.URL("ikvmres", "gen", ClassLoaderWrapper.GetGenericClassLoaderId(this), "/" + name);
                }
            }
#endif
            return null;
        }
#endif
    }
}
