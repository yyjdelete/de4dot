﻿/*
    Copyright (C) 2011-2012 de4dot@gmail.com

    This file is part of de4dot.

    de4dot is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    de4dot is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with de4dot.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.MyStuff;

namespace de4dot.code.deobfuscators.CodeVeil {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "CodeVeil";
		public const string THE_TYPE = "cv";
		const string DEFAULT_REGEX = @"!^[A-Za-z]{1,2}$&" + DeobfuscatorBase.DEFAULT_VALID_NAME_REGEX;

		public DeobfuscatorInfo()
			: base(DEFAULT_REGEX) {
		}

		public override string Name {
			get { return THE_NAME; }
		}

		public override string Type {
			get { return THE_TYPE; }
		}

		public override IDeobfuscator createDeobfuscator() {
			return new Deobfuscator(new Deobfuscator.Options {
				ValidNameRegex = validNameRegex.get(),
			});
		}

		protected override IEnumerable<Option> getOptionsInternal() {
			return new List<Option>() {
			};
		}
	}

	class Deobfuscator : DeobfuscatorBase {
		Options options;
		string obfuscatorName = DeobfuscatorInfo.THE_NAME;
		bool foundKillType = false;

		MainType mainType;
		MethodsDecrypter methodsDecrypter;
		ProxyDelegateFinder proxyDelegateFinder;
		StringDecrypter stringDecrypter;
		AssemblyResolver assemblyResolver;

		internal class Options : OptionsBase {
		}

		public override string Type {
			get { return DeobfuscatorInfo.THE_TYPE; }
		}

		public override string TypeLong {
			get { return DeobfuscatorInfo.THE_NAME; }
		}

		public override string Name {
			get { return obfuscatorName; }
		}

		public Deobfuscator(Options options)
			: base(options) {
			this.options = options;
		}

		protected override int detectInternal() {
			int val = 0;

			int sum = toInt32(mainType.Detected) +
					toInt32(methodsDecrypter.Detected) +
					toInt32(stringDecrypter.Detected) +
					toInt32(proxyDelegateFinder.Detected);
			if (sum > 0)
				val += 100 + 10 * (sum - 1);
			if (foundKillType)
				val += 10;

			return val;
		}

		protected override void scanForObfuscator() {
			findKillType();
			mainType = new MainType(module);
			mainType.find();
			proxyDelegateFinder = new ProxyDelegateFinder(module, mainType);
			proxyDelegateFinder.findDelegateCreator();
			methodsDecrypter = new MethodsDecrypter(mainType);
			methodsDecrypter.find();
			stringDecrypter = new StringDecrypter(module, mainType);
			stringDecrypter.find();
			var version = detectVersion();
			if (!string.IsNullOrEmpty(version))
				obfuscatorName = obfuscatorName + " " + version;
		}

		string detectVersion() {
			if (mainType.Detected) {
				switch (mainType.Version) {
				case ObfuscatorVersion.Unknown:
					return null;

				case ObfuscatorVersion.V3:
					return "3.x";

				case ObfuscatorVersion.V4_0:
					return "4.0";

				case ObfuscatorVersion.V4_1:
					return "4.1";

				case ObfuscatorVersion.V5_0:
					return "5.0";

				default:
					throw new ApplicationException("Unknown version");
				}
			}

			return null;
		}

		void findKillType() {
			foreach (var type in module.Types) {
				if (type.FullName == "____KILL") {
					addTypeToBeRemoved(type, "KILL type");
					foundKillType = true;
					break;
				}
			}
		}

		public override bool getDecryptedModule(ref byte[] newFileData, ref Dictionary<uint, DumpedMethod> dumpedMethods) {
			if (!methodsDecrypter.Detected)
				return false;

			var fileData = DeobUtils.readModule(module);
			if (!methodsDecrypter.decrypt(fileData, ref dumpedMethods))
				return false;

			newFileData = fileData;
			return true;
		}

		public override IDeobfuscator moduleReloaded(ModuleDefinition module) {
			var newOne = new Deobfuscator(options);
			newOne.setModule(module);
			newOne.mainType = new MainType(module, mainType);
			newOne.methodsDecrypter = new MethodsDecrypter(mainType, methodsDecrypter);
			newOne.stringDecrypter = new StringDecrypter(module, newOne.mainType, stringDecrypter);
			newOne.proxyDelegateFinder = new ProxyDelegateFinder(module, newOne.mainType, proxyDelegateFinder);
			return newOne;
		}

		public override void deobfuscateBegin() {
			base.deobfuscateBegin();

			if (Operations.DecryptStrings != OpDecryptString.None) {
				stringDecrypter.initialize();
				staticStringInliner.add(stringDecrypter.DecryptMethod, (method, args) => {
					return stringDecrypter.decrypt((int)args[0]);
				});
				DeobfuscatedFile.stringDecryptersAdded();
			}

			assemblyResolver = new AssemblyResolver(module);
			assemblyResolver.initialize();
			dumpEmbeddedAssemblies();

			proxyDelegateFinder.initialize();
			proxyDelegateFinder.find();
		}

		void dumpEmbeddedAssemblies() {
			foreach (var info in assemblyResolver.AssemblyInfos)
				DeobfuscatedFile.createAssemblyFile(info.data, info.simpleName, info.extension);
			addResourceToBeRemoved(assemblyResolver.BundleDataResource, "Embedded assemblies resource");
			addResourceToBeRemoved(assemblyResolver.BundleXmlFileResource, "Embedded assemblies XML file resource");
		}

		public override void deobfuscateMethodBegin(blocks.Blocks blocks) {
			proxyDelegateFinder.deobfuscate(blocks);
			base.deobfuscateMethodBegin(blocks);
		}

		public override void deobfuscateEnd() {
			removeProxyDelegates(proxyDelegateFinder, false);	//TODO: Should be 'true'
			base.deobfuscateEnd();
		}

		public override IEnumerable<string> getStringDecrypterMethods() {
			var list = new List<string>();
			if (stringDecrypter.DecryptMethod != null)
				list.Add(stringDecrypter.DecryptMethod.MetadataToken.ToInt32().ToString("X8"));
			return list;
		}
	}
}
