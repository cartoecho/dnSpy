﻿/*
    Copyright (C) 2014-2018 de4dot@gmail.com

    This file is part of dnSpy

    dnSpy is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    dnSpy is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with dnSpy.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using dnlib.DotNet;
using dnSpy.AsmEditor.Compiler.MDEditor;

namespace dnSpy.AsmEditor.Compiler {
	struct ModulePatcher {
		readonly RawModuleBytes moduleData;
		readonly bool isFileLayout;
		readonly IAssembly tempAssembly;
		readonly TypeDef nonNestedEditedTypeOrNull;
		readonly bool makeEverythingPublic;

		public ModulePatcher(RawModuleBytes moduleData, bool isFileLayout, IAssembly tempAssembly, TypeDef nonNestedEditedTypeOrNull, bool makeEverythingPublic) {
			this.moduleData = moduleData;
			this.isFileLayout = isFileLayout;
			this.tempAssembly = tempAssembly;
			this.nonNestedEditedTypeOrNull = nonNestedEditedTypeOrNull;
			this.makeEverythingPublic = makeEverythingPublic;
		}

		public unsafe bool Patch(ModuleDef module, out RawModuleBytes newModuleData) {
			var moduleData = this.moduleData;

			// NOTE: We can't remove the type from the corlib (eg. mscorlib) because the compiler
			// (Roslyn) won't recognize it as the corlib if it has any AssemblyRefs.
			// A possible fix is to add a new netmodule to the corlib assembly.
			bool fixTypeDefRefs = nonNestedEditedTypeOrNull != null &&
				MDPatcherUtils.ExistsInMetadata(nonNestedEditedTypeOrNull) &&
				MDPatcherUtils.ReferencesModule(module, nonNestedEditedTypeOrNull?.Module) &&
				!module.Assembly.IsCorLib();
			if (makeEverythingPublic || fixTypeDefRefs) {
				using (var md = MDPatcherUtils.TryCreateMetadata(moduleData, isFileLayout)) {
					if (makeEverythingPublic) {
						bool success = new MetadataFixer(moduleData, md).MakePublic();
						if (!success) {
							newModuleData = null;
							return false;
						}
					}
					if (fixTypeDefRefs) {
						var mdEditor = new MetadataEditor(moduleData, md);
						var patcher = new MDEditorPatcher(moduleData, mdEditor, tempAssembly, nonNestedEditedTypeOrNull);
						patcher.Patch(module);
						if (mdEditor.MustRewriteMetadata()) {
							var stream = new MDWriterMemoryStream();
							new MDWriter(moduleData, mdEditor, stream).Write();
							NativeMemoryRawModuleBytes newRawData = null;
							try {
								newRawData = new NativeMemoryRawModuleBytes((int)stream.Length);
								stream.CopyTo((IntPtr)newRawData.Pointer, newRawData.Size);
								moduleData = newRawData;
							}
							catch {
								newRawData?.Dispose();
								throw;
							}
						}
					}
				}
			}

			newModuleData = moduleData;
			return true;
		}
	}
}
