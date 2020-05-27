/*
  Copyright (C) 2020 Marko Kokol

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
using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Security.AccessControl;
using System.Text;

static class Java_sun_nio_fs_NetFileSystemProvider
{
    [SecurityCritical]
    public static FileStream createFile0(string path, FileMode fileMode, FileSystemRights fileSystemRights, FileShare fileShare, int bufferSize, FileOptions fileOptions)
    {
#if !FIRST_PASS
        System.Security.AccessControl.FileSecurity security;
        if (System.IO.File.Exists(path))
        {
            System.IO.FileInfo file = new FileInfo(path);
            security = file.GetAccessControl();
        }
        else
        {
            System.IO.DirectoryInfo directory = new System.IO.DirectoryInfo(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)));
            var parentSecurity = directory.GetAccessControl().GetAccessRules(true, true, typeof(System.Security.Principal.NTAccount));
            security = new System.Security.AccessControl.FileSecurity();
            foreach (object ruleObject in parentSecurity)
            {
                var rule = ruleObject as FileSystemAccessRule;
                security.AddAccessRule(new FileSystemAccessRule(rule.IdentityReference, rule.FileSystemRights, rule.AccessControlType));
            }

            security.SetAccessRuleProtection(false, false);
        }

        return FileSystemAclExtensions.Create(new FileInfo(path), fileMode, fileSystemRights, fileShare, bufferSize, fileOptions, security);
#else
        return null;
#endif
    }
}

