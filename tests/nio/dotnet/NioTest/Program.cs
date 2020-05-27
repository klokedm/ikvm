//using semtests;
using System;
using System.IO;
using System.Security.AccessControl;

namespace NioTest
{
    class Program
    {
        static void Main(string[] args)
        {
            FileInfo fi = new FileInfo(@"C:\Users\marko\source\repos\WebGalis\src\Tests.Core\Semantika.WebGalis.Lucene.Tests\bin\Release\netcoreapp3.1\test_index_sem_wg_lucene_integtest_facets\FTS_M");
            System.Console.WriteLine(fi.Attributes);
            System.Console.WriteLine(FileAttributes.Directory);
            System.Console.WriteLine(FileAttributes.Directory & fi.Attributes);
        }
    }
}
