﻿using System;
using System.Reflection;
using System.Reflection.Emit;
#pragma warning disable 0618

namespace Harmony
{
    [Obsolete("Harmony.HarmonyInstance is obsolete. Please use HarmonyLib.Harmony instead. This will be removed in a future update.", true)]
    public class HarmonyInstance : HarmonyLib.Harmony
	{
		[Obsolete("Harmony.HarmonyInstance is obsolete. Please use HarmonyLib.Harmony instead. This will be removed in a future update.", true)]
		public HarmonyInstance(string id) : base(id) { }

		[Obsolete("Harmony.HarmonyInstance.Create is obsolete. Please use the HarmonyLib.Harmony Constructor instead. This will be removed in a future update.", true)]
		public static HarmonyInstance Create(string id)
		{
			if (id == null) throw new Exception("id cannot be null");
			return new HarmonyInstance(id);
		}

		[Obsolete("Harmony.HarmonyInstance.Patch is obsolete. Please use HarmonyLib.Harmony.Patch instead. This will be removed in a future update.", true)]
		public DynamicMethod Patch(MethodBase original, HarmonyMethod prefix = null, HarmonyMethod postfix = null, HarmonyMethod transpiler = null)
		{
			base.Patch(original, prefix, postfix, transpiler);
			return null;
		}

        public void Unpatch(MethodBase original, HarmonyPatchType type, string harmonyID = null) => Unpatch(original, (HarmonyLib.HarmonyPatchType)type, harmonyID);
	}
}
