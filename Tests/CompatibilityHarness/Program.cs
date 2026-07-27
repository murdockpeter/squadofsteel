using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace SquadOfSteel.CompatibilityHarness
{
    internal static class Program
    {
        static readonly HashSet<string> ExpectedTargets = new HashSet<string>(StringComparer.Ordinal)
        {
            "OfficialUnits.Load",
            "TurnManager.NextTurn",
            "UIManager.Start",
            "Unit.GetPotentialDamage",
            "UnitGO.Attack",
            "UnitGO.AttackUnit",
            "UnitGO.DestroyUnit",
            "UnitGO.Retaliate",
            "UnitGO.UpdateCounter"
        };

        static int Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.Error.WriteLine("Usage: SquadOfSteel.CompatibilityHarness <SquadOfSteel.dll> <HoS Managed directory>");
                return 2;
            }

            string modPath = Path.GetFullPath(args[0]);
            if (!File.Exists(modPath))
            {
                Console.Error.WriteLine("Mod assembly was not found: " + modPath);
                return 2;
            }

            string repositoryRoot = Directory.GetParent(
                Directory.GetParent(
                    Directory.GetParent(modPath).FullName).FullName).FullName;
            string librariesPath = Path.Combine(repositoryRoot, "Libraries");
            string gameManagedPath = Path.GetFullPath(args[1]);
            if (!Directory.Exists(gameManagedPath))
            {
                Console.Error.WriteLine("HoS Managed directory was not found: " + gameManagedPath);
                return 2;
            }

            AppDomain.CurrentDomain.AssemblyResolve += (sender, eventArgs) =>
            {
                string assemblyFile = new AssemblyName(eventArgs.Name).Name + ".dll";
                string localCandidate = Path.Combine(librariesPath, assemblyFile);
                if (File.Exists(localCandidate))
                {
                    return Assembly.LoadFrom(localCandidate);
                }

                string gameCandidate = Path.Combine(gameManagedPath, assemblyFile);
                return File.Exists(gameCandidate) ? Assembly.LoadFrom(gameCandidate) : null;
            };

            try
            {
                Assembly modAssembly = Assembly.LoadFrom(modPath);
                var resolvedTargets = new HashSet<string>(StringComparer.Ordinal);
                foreach (Type patchType in modAssembly.GetTypes())
                {
                    var classAttributes = patchType
                        .GetCustomAttributes(typeof(HarmonyPatch), true)
                        .Cast<HarmonyPatch>()
                        .Select(attribute => attribute.info)
                        .ToList();

                    var attributedPatchMethods = patchType
                        .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                        .Select(method => new
                        {
                            Method = method,
                            Attributes = method
                                .GetCustomAttributes(typeof(HarmonyPatch), true)
                                .Cast<HarmonyPatch>()
                                .Select(attribute => attribute.info)
                                .ToList()
                        })
                        .Where(item => item.Attributes.Count > 0)
                        .ToList();

                    if (classAttributes.Count == 0 && attributedPatchMethods.Count == 0)
                    {
                        continue;
                    }

                    if (attributedPatchMethods.Count == 0)
                    {
                        ResolveTarget(HarmonyMethod.Merge(classAttributes), resolvedTargets);
                        continue;
                    }

                    foreach (var patchMethod in attributedPatchMethods)
                    {
                        var combined = new List<HarmonyMethod>(classAttributes);
                        combined.AddRange(patchMethod.Attributes);
                        ResolveTarget(HarmonyMethod.Merge(combined), resolvedTargets);
                    }
                }

                var missing = ExpectedTargets.Except(resolvedTargets).OrderBy(name => name).ToArray();
                var unexpected = resolvedTargets.Except(ExpectedTargets).OrderBy(name => name).ToArray();

                Console.WriteLine("Harmony version: " + typeof(Harmony).Assembly.GetName().Version);
                Console.WriteLine("Resolved original methods: " + resolvedTargets.Count);
                foreach (string target in resolvedTargets.OrderBy(name => name))
                {
                    Console.WriteLine(" - " + target);
                }

                if (missing.Length > 0 || unexpected.Length > 0)
                {
                    foreach (string target in missing)
                    {
                        Console.Error.WriteLine("Missing expected patch target: " + target);
                    }

                    foreach (string target in unexpected)
                    {
                        Console.Error.WriteLine("Unexpected patch target: " + target);
                    }

                    return 1;
                }

                Console.WriteLine("Harmony target compatibility test passed.");
                return 0;
            }
            catch (Exception exception)
            {
                for (Exception current = exception; current != null; current = current.InnerException)
                {
                    Console.Error.WriteLine(current.GetType().FullName + ": " + current.Message);
                    Console.Error.WriteLine(current.StackTrace);
                }

                return 1;
            }
        }

        static void ResolveTarget(HarmonyMethod patchInfo, ISet<string> resolvedTargets)
        {
            if (patchInfo.declaringType == null || string.IsNullOrEmpty(patchInfo.methodName))
            {
                throw new InvalidOperationException("Harmony patch metadata did not define a declaring type and method name.");
            }

            MethodInfo target = AccessTools.DeclaredMethod(
                patchInfo.declaringType,
                patchInfo.methodName,
                patchInfo.argumentTypes);
            if (target == null)
            {
                throw new MissingMethodException(
                    patchInfo.declaringType.FullName,
                    patchInfo.methodName);
            }

            resolvedTargets.Add(target.DeclaringType.FullName + "." + target.Name);
        }
    }
}
