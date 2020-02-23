using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Permissions;
using System.Text;

[assembly: SecurityPermission( SecurityAction.RequestMinimum, SkipVerification = true )]
[assembly: IgnoresAccessChecksTo( "Assembly-CSharp" )]
namespace System.Runtime.CompilerServices
{
    [AttributeUsage( AttributeTargets.Assembly, AllowMultiple = true )]
    public class IgnoresAccessChecksToAttribute : Attribute
    {
        public IgnoresAccessChecksToAttribute( string assemblyName )
        {
            this.AssemblyName = assemblyName;
        }

        public string AssemblyName { get; }
    }
}
