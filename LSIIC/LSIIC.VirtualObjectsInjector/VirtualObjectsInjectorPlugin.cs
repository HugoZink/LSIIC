﻿using Anvil;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using FistVR;
using HarmonyLib;
using RUST.Steamworks;
using Steamworks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

[assembly: AssemblyVersion("1.3")]
namespace LSIIC.VirtualObjectsInjector
{
	[BepInPlugin("net.block57.lsiic.virtualobjectsinjector", "LSIIC - Virtual Objects Injector", "1.3")]
	public class VirtualObjectsInjectorPlugin : BaseUnityPlugin
	{
		public static ManualLogSource Logger { get; set; }

		private void Awake()
		{
			Logger = base.Logger;

			Harmony.CreateAndPatchAll(typeof(VirtualObjectsInjectorPlugin));
		}

		//injects FVRObject(s) and ItemSpawnerID(s) into IM
		[HarmonyPatch(typeof(IM), "GenerateItemDBs")]
		[HarmonyPostfix]
		public static void IM_GenerateItemDBs(IM __instance, Dictionary<string, ItemSpawnerID> ___SpawnerIDDic)
		{
			Uri StreamingAssetsUri = new Uri(Application.streamingAssetsPath + "\\dummy");
			if (!Directory.Exists(Paths.GameRootPath + @"\VirtualObjects"))
			{
				Logger.LogWarning("VirtualObjectsInjector has no H3VR/VirtualObjects folder. No objects will be loaded.");
				return;
			}

			foreach (string file in Directory.GetFiles(Paths.GameRootPath + @"\VirtualObjects", "*", SearchOption.AllDirectories))
			{
				//field existence checks
				bool FVROhasItemID = AccessTools.Field(typeof(FVRObject), "ItemID") != null;
				bool FVROhasCategory = AccessTools.Field(typeof(FVRObject), "Category") != null;
				bool FVROhasTagEra = AccessTools.Field(typeof(FVRObject), "TagEra") != null;
				bool FVROhasTagFirearmSize = AccessTools.Field(typeof(FVRObject), "TagFirearmSize") != null;
				bool FVROhasTagFirearmAction = AccessTools.Field(typeof(FVRObject), "TagFirearmAction") != null;
				bool FVROhasTagFirearmFiringModes = AccessTools.Field(typeof(FVRObject), "TagFirearmFiringModes") != null;
				bool FVROhasTagFirearmFeedOption = AccessTools.Field(typeof(FVRObject), "TagFirearmFeedOption") != null;
				bool FVROhasTagFirearmMounts = AccessTools.Field(typeof(FVRObject), "TagFirearmMounts") != null;
				bool FVROhasTagAttachmentMount = AccessTools.Field(typeof(FVRObject), "TagAttachmentMount") != null;
				bool FVROhasTagAttachmentFeature = AccessTools.Field(typeof(FVRObject), "TagAttachmentFeature") != null;

				if (Path.GetFileName(file) != Path.GetFileNameWithoutExtension(file) || Path.GetFileName(file).Contains("VirtualObjects"))
					continue;

				string relativeFile = StreamingAssetsUri.MakeRelativeUri(new Uri(file)).ToString();

				AnvilCallback<AssetBundle> bundle = AnvilManager.GetBundleAsync(relativeFile);
				bundle.AddCallback(delegate
				{
					if (bundle.Result != null)
					{
						Logger.Log(LogLevel.Info, $"Injecting FVRObject(s) and ItemSpawnerID(s) from {Path.GetFileName(file)}");

						int objectsFound = 0;

						foreach (FVRObject fvrObj in bundle.Result.LoadAllAssets<FVRObject>())
						{
							//allows the original bundle path to be ignored, a bit cursed
							FieldInfo anvilPrefab = AccessTools.Field(typeof(FVRObject), "m_anvilPrefab");
							AssetID original = (AssetID)anvilPrefab.GetValue(fvrObj);
							original.Bundle = relativeFile;
							anvilPrefab.SetValue(fvrObj, original);

							if (FVROhasItemID)
								IM.OD.Add(fvrObj.ItemID, fvrObj);
							if (FVROhasCategory)
								__instance.odicTagCategory.AddOrCreate(fvrObj.Category).Add(fvrObj);
							if (FVROhasTagEra)
								__instance.odicTagFirearmEra.AddOrCreate(fvrObj.TagEra).Add(fvrObj);
							if (FVROhasTagFirearmSize)
								__instance.odicTagFirearmSize.AddOrCreate(fvrObj.TagFirearmSize).Add(fvrObj);
							if (FVROhasTagFirearmAction)
								__instance.odicTagFirearmAction.AddOrCreate(fvrObj.TagFirearmAction).Add(fvrObj);

							if (FVROhasTagFirearmFiringModes)
								foreach (FVRObject.OTagFirearmFiringMode firingMode in fvrObj.TagFirearmFiringModes)
									__instance.odicTagFirearmFiringMode.AddOrCreate(firingMode).Add(fvrObj);
							if (FVROhasTagFirearmFeedOption)
								foreach (FVRObject.OTagFirearmFeedOption feedOption in fvrObj.TagFirearmFeedOption)
									__instance.odicTagFirearmFeedOption.AddOrCreate(feedOption).Add(fvrObj);
							if (FVROhasTagFirearmMounts)
								foreach (FVRObject.OTagFirearmMount mount in fvrObj.TagFirearmMounts)
									__instance.odicTagFirearmMount.AddOrCreate(mount).Add(fvrObj);

							if (FVROhasTagAttachmentMount)
								__instance.odicTagAttachmentMount.AddOrCreate(fvrObj.TagAttachmentMount).Add(fvrObj);
							if (FVROhasTagAttachmentFeature)
								__instance.odicTagAttachmentFeature.AddOrCreate(fvrObj.TagAttachmentFeature).Add(fvrObj);

							objectsFound++;
						}
						foreach (ItemSpawnerID id in bundle.Result.LoadAllAssets<ItemSpawnerID>())
						{
							IM.CD[id.Category].Add(id);
							IM.SCD[id.SubCategory].Add(id);
							if (!___SpawnerIDDic.ContainsKey(id.ItemID))
								___SpawnerIDDic[id.ItemID] = id;
							else
								Logger.LogError($"ItemID {id.ItemID} from {Path.GetFileName(file)} already exists in SpawnerIDDic! You need to change the ItemID in your ItemSpawnerID to something else.");
						}

						if (objectsFound > 0)
						{
							Logger.LogWarning($"{objectsFound} object(s) were loaded through VirtualObjectsInjector!");
#if !DEBUG
							Logger.LogWarning("This plugin has been deprecated and has been integrated into H3VR.Sideloader.");
							Logger.LogWarning("It should only be used for hassle-free rapid prototyping from Unity into H3VR.");
							Logger.LogWarning("Please ask the creators of the asset bundle to covert their object(s) to a Sideloader or Deli mod.");
#endif
						}
					}
					else
						Logger.LogError("AssetBundle is somehow null, what did you do?");
				});
			}
		}

		/*
		 * Skiddie prevention
		 */
		[HarmonyPatch(typeof(HighScoreManager), nameof(HighScoreManager.UpdateScore), new Type[] { typeof(string), typeof(int), typeof(Action<int, int>) })]
		[HarmonyPatch(typeof(HighScoreManager), nameof(HighScoreManager.UpdateScore), new Type[] { typeof(SteamLeaderboard_t), typeof(int) })]
		[HarmonyPrefix]
		public static bool HSM_UpdateScore()
		{
			return false;
		}
	}

	public static class DictionaryExtension
	{
		public static TValue AddOrCreate<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key) where TValue : new()
		{
			if (!dictionary.ContainsKey(key))
				dictionary.Add(key, new TValue());
			return dictionary[key];
		}
	}
}
