// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Roslyn.VisualStudio.Setup;

[assembly: ProvideRoslynBindingRedirection("Microsoft.CodeAnalysis.Scripting.dll")]
[assembly: ProvideRoslynBindingRedirection("Microsoft.CodeAnalysis.CSharp.Scripting.dll")]
// [assembly: ProvideRoslynBindingRedirection("Microsoft.CodeAnalysis.VisualBasic.Scripting.dll")]

[assembly: ProvideRoslynBindingRedirection("Microsoft.CodeAnalysis.CSharp.InteractiveEditorFeatures.dll")]
// [assembly: ProvideRoslynBindingRedirection("Microsoft.CodeAnalysis.VisualBasic.InteractiveEditorFeatures.dll")]

[assembly: ProvideRoslynBindingRedirection("Microsoft.CodeAnalysis.InteractiveFeatures.dll")]
