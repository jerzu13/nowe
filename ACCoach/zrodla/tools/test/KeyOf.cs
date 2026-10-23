// Writes the public key of an assembly to a file (input for delay-signing SpeechStub.cs). Test/build tool only.
using System;
using System.IO;
using System.Reflection;
static class KeyOf
{
    static void Main(string[] a)
    {
        AssemblyName n = AssemblyName.GetAssemblyName(a[0]);
        File.WriteAllBytes(a[1], n.GetPublicKey());
        Console.WriteLine(n.FullName);
    }
}
