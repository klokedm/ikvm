/*
  Copyright (C) 2007 Jeroen Frijters

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

  ====

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
package sun.nio.ch;

import java.io.File;
import java.io.FileDescriptor;
import java.io.IOException;

public class FileKey
{
    private String path;

    public static FileKey create(FileDescriptor fd)
    {
        FileKey fk = new FileKey();
        fk.path = fd.getName();
        if (fk.path == null || fk.path.equals("")) {
            //We consider this to be safer than a static string -> this way a lock is not obtained for 
            //streams with unknown names, such as network streams
            fk.path = java.util.UUID.randomUUID().toString();
        } else {
            try {
            //We assume the name is a valid path that can be converted to the canonical path
                fk.path = new File(fk.path).getCanonicalPath();
            } catch (IOException ex) {
                //There is something wrong -> if we have a name stored in FD, it should be a valid filename. 
                //The IOException was previously swallowed here. This can cause the code relying on this to fail 
                //in unintuitive ways, so we are rethrowing here to make it clear what is happening
                throw new IllegalStateException("A file descriptor with an invalid name was provided to the FileKey.");
            }
        }
        return fk;
    }

    public int hashCode()
    {
        return path.hashCode();
    }

    public boolean equals(Object obj)
    {
        return obj == this || (obj instanceof FileKey && ((FileKey)obj).path.equals(path));
    }
}
