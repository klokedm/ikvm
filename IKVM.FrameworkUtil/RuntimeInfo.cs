/*
  Copyright (C) 2020 Marko Kokol, Semantika d.o.o.

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

  Marko Kokol
  marko.kokol@semantika.eu
  
*/

using System.IO;
using System.Reflection;

namespace IKVM.FrameworkUtil
{
    ////////////////////////////////////////////////////////////////////////////////////////////////////
    /// <summary>
    /// A simple helper extension class providing information about the current runtime environment
    /// to make it easier to configure the build environment.
    /// 
    /// This is a separate lightweight library to make it easy to reference in other projects.
    /// </summary>
    ///
    /// <remarks>   Marko Kokol, Semantika d.o.o.,. </remarks>
    ////////////////////////////////////////////////////////////////////////////////////////////////////
    public static class RuntimeInfo
    {
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Location of the reference core library. 
        ///             XXX:THIS IS A HACK. We need to introduce either code to support the switching 
        ///             between different SDK targets on load or some SDK locator logic
        /// </summary>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        public static readonly string ReferenceCoreLibLocation = $"{Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)}\\..\\netstandard2.1\\refs\\netstandard.dll";

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   The reference core library full name. </summary>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        public static readonly string ReferenceCoreLibFullName = AssemblyName.GetAssemblyName(ReferenceCoreLibLocation).FullName;

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Name of the reference core library. </summary>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        public static readonly string ReferenceCoreLibName = AssemblyName.GetAssemblyName(ReferenceCoreLibLocation).Name;

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Name of the private core library. </summary>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        public static readonly string PrivateCoreLibName = typeof(object).Assembly.GetName().Name;

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>
        /// A string extension method that verifies if the provided library name is part of the core
        /// libraries.
        /// </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="libraryName">  The name of the assembly to check. </param>
        ///
        /// <returns>   True if the library is part of the runtime core, false if not. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        public static bool IsPartOfCore(this string libraryName)
        {
            return IsReferenceCoreLib(libraryName) || IsPrivateCoreLib(libraryName);
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>
        /// A string extension method that verifies if the provided library name is the reference core
        /// library.
        /// </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="libraryName">  The name of the assembly to check. </param>
        ///
        /// <returns>   True if the library is the reference core library, false if not. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        public static bool IsReferenceCoreLib(this string libraryName)
        {
            //TODO: Should we change this for other OS types?
            return libraryName != null && (libraryName.ToLower() == ReferenceCoreLibName.ToLower() || libraryName.ToLower() == ReferenceCoreLibName.ToLower() + ".dll");
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>
        /// A string extension method that verifies if the provided library name is the private core
        /// library. Specifically it will verify whether this is the same library that defines the
        /// System.Object type.
        /// </summary>
        ///
        /// <remarks>   Semantika d.o.o.,. </remarks>
        ///
        /// <param name="libraryName">  The name of the assembly to check. </param>
        ///
        /// <returns>   True if the library is the private core library, false if not. </returns>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        public static bool IsPrivateCoreLib(this string libraryName)
        {
            return libraryName != null && (libraryName.ToLower() == PrivateCoreLibName.ToLower() || libraryName.ToLower() == PrivateCoreLibName.ToLower() + ".dll");
        }
    }
}
