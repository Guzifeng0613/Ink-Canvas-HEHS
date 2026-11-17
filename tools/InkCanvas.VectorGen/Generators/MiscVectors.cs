using System.Numerics;
using InkCanvas.Core.Imaging;
using InkCanvas.Core.Input;
using InkCanvas.Core.Replay;
using InkCanvas.Core.Settings;
using InkCanvas.Core.Theming;
using InkCanvas.Core.Update;
using InkCanvas.Core.Versioning;
using InkCanvas.Platform.Input;
using InkCanvas.Platform.Windows;

internal static class MiscVectors
{
    public static void Write(string dir)
    {
        WriteTouch(dir);
        WritePalm(dir);
        WriteReplayClock(dir);
        WriteReplaySession(dir);
        WriteCompositing(dir);
        WriteTheme(dir);
        WriteVersion(dir);
        WriteUpdatePolicy(dir);
        WriteReleaseFeed(dir);
    }

    private static void WriteTouch(string dir)
    {
        var advanced = new AdvancedSettings
        {
            IsSpecialScreen = true,
            TouchMultiplier = 0.3,
            NibModeBoundsWidth = 10,
            FingerModeBoundsWidth = 30,
            NibModeBoundsWidthThresholdValue = 2.5,
            FingerModeBoundsWidthThresholdValue = 2.5,
            NibModeBoundsWidthEraserSize = 0.8,
            FingerModeBoundsWidthEraserSize = 0.8
        };
        var plain = new AdvancedSettings
        {
            IsSpecialScreen = false,
            TouchMultiplier = 0.25,
            NibModeBoundsWidth = 10,
            FingerModeBoundsWidth = 30,
            NibModeBoundsWidthThresholdValue = 2.5,
            FingerModeBoundsWidthThresholdValue = 2.5,
            NibModeBoundsWidthEraserSize = 0.8,
            FingerModeBoundsWidthEraserSize = 0.8
        };

        var cases = new List<object>();
        AddMeasure(cases, "measure-normal", 10, 20, false);
        AddMeasure(cases, "measure-quadir", 10, 20, true);
        AddMeasure(cases, "measure-zero", 0, 0, true);

        AddClassify(cases, "classify-draw", 5, plain, false, false, false, true, false);
        AddClassify(cases, "classify-draw-no-point-eraser", 5, plain, false, false, false, false, false);
        AddClassify(cases, "classify-null-advanced", 100, null, false, false, false, true, false);
        AddClassify(cases, "classify-erase-stroke", 40, plain, false, false, false, true, false);
        AddClassify(cases, "classify-erase-point", 100, plain, false, false, false, true, false);
        AddClassify(cases, "classify-erase-point-special", 100, advanced, false, false, false, true, false);
        AddClassify(cases, "classify-multiplier-zero-disables-eraser", 100, new AdvancedSettings
        {
            IsSpecialScreen = true, TouchMultiplier = 0, FingerModeBoundsWidth = 30,
            NibModeBoundsWidth = 10, NibModeBoundsWidthThresholdValue = 2.5,
            FingerModeBoundsWidthThresholdValue = 2.5, NibModeBoundsWidthEraserSize = 0.8,
            FingerModeBoundsWidthEraserSize = 0.8
        }, false, false, false, true, false);
        AddClassify(cases, "classify-nib-stroke", 15, plain, true, false, false, true, false);
        AddClassify(cases, "classify-nib-point", 30, plain, true, false, false, true, false);
        AddClassify(cases, "classify-force-eraser-ignore", 100, plain, false, true, false, true, false);
        AddClassify(cases, "classify-force-eraser-drawing-shape", 100, plain, false, true, true, true, false);
        AddClassify(cases, "classify-ppt-gesture", 40, plain, false, false, false, true, true);

        JsonWriter.Write(dir, "touch-classifier.json", new
        {
            id = "touch-classifier",
            entry = "harmony/core/src/main/ets/input/TouchContactClassifier.ts",
            cases
        });
    }

    private static void AddMeasure(List<object> cases, string id, double width, double height, bool quadIr)
    {
        cases.Add(new
        {
            id,
            fn = "measureContactWidth",
            input = new object[] { width, height, quadIr },
            expected = TouchContactClassifier.MeasureContactWidth(width, height, quadIr),
            tolerance = 1e-4
        });
    }

    private static void AddClassify(List<object> cases, string id, double contactWidth, AdvancedSettings? advanced,
        bool nibMode, bool forceEraser, bool drawingShape, bool forcePointEraser, bool pptGestureEligible)
    {
        var decision = TouchContactClassifier.Classify(
            contactWidth, advanced!, nibMode, forceEraser, drawingShape, forcePointEraser, pptGestureEligible);
        cases.Add(new
        {
            id,
            fn = "classifyContact",
            input = new object[]
            {
                new
                {
                    contactWidth,
                    advanced = advanced is null ? null : AdvancedJson(advanced),
                    nibMode,
                    forceEraser,
                    isDrawingShape = drawingShape,
                    forcePointEraser,
                    pptGestureEligible
                }
            },
            expected = new { intent = (int)decision.Intent, eraserDiameter = decision.EraserDiameter },
            tolerance = 1e-4
        });
    }

    private static object AdvancedJson(AdvancedSettings a) => new
    {
        isSpecialScreen = a.IsSpecialScreen,
        isQuadIR = a.IsQuadIR,
        touchMultiplier = a.TouchMultiplier,
        nibModeBoundsWidth = a.NibModeBoundsWidth,
        fingerModeBoundsWidth = a.FingerModeBoundsWidth,
        nibModeBoundsWidthThresholdValue = a.NibModeBoundsWidthThresholdValue,
        fingerModeBoundsWidthThresholdValue = a.FingerModeBoundsWidthThresholdValue,
        nibModeBoundsWidthEraserSize = a.NibModeBoundsWidthEraserSize,
        fingerModeBoundsWidthEraserSize = a.FingerModeBoundsWidthEraserSize
    };

    private static void WritePalm(string dir)
    {
        var advanced = new AdvancedSettings
        {
            IsSpecialScreen = false, TouchMultiplier = 0.25, FingerModeBoundsWidth = 30,
            NibModeBoundsWidth = 10, NibModeBoundsWidthThresholdValue = 2.5,
            FingerModeBoundsWidthThresholdValue = 2.5, NibModeBoundsWidthEraserSize = 0.8,
            FingerModeBoundsWidthEraserSize = 0.8
        };
        var events = new (string Type, uint Id, OverlayPointerKind Kind, float W, float H)[]
        {
            ("down", 1, OverlayPointerKind.Pen, 0, 0),
            ("down", 2, OverlayPointerKind.Touch, 8, 8),
            ("down", 3, OverlayPointerKind.Touch, 40, 35),
            ("up", 1, OverlayPointerKind.Pen, 0, 0),
            ("up", 3, OverlayPointerKind.Touch, 40, 35),
            ("up", 2, OverlayPointerKind.Touch, 8, 8)
        };
        var cases = new List<object>
        {
            PalmCase("palm-sequence", events, 30f, true, advanced, false, false, true, false),
            PalmCase("palm-no-priority", events, 30f, false, advanced, false, false, true, false),
            PalmCase("palm-threshold-boundary", new[]
            {
                ("down", 1u, OverlayPointerKind.Touch, 30f, 10f),
                ("up", 1u, OverlayPointerKind.Touch, 30f, 10f)
            }, 30f, true, null, false, false, true, false)
        };
        JsonWriter.Write(dir, "palm.json", new
        {
            id = "palm",
            entry = "harmony/core/src/main/ets/input/PalmRejector.ts",
            cases
        });
    }

    private static object PalmCase(string id,
        (string Type, uint Id, OverlayPointerKind Kind, float W, float H)[] events,
        float threshold, bool penPriority, AdvancedSettings? advanced,
        bool nibMode, bool forceEraser, bool forcePointEraser, bool pptEligible)
    {
        var rejector = new PalmRejector
        {
            PalmContactThreshold = threshold,
            PenHasPriority = penPriority,
            Advanced = advanced,
            NibMode = nibMode,
            ForceEraser = forceEraser,
            ForcePointEraser = forcePointEraser,
            PptGestureEligible = pptEligible
        };
        var results = new List<object>();
        foreach (var e in events)
        {
            var p = new OverlayPointer(e.Id, 0, 0, 0.5f, e.W, e.H, e.Kind, 0);
            if (e.Type == "down")
            {
                rejector.OnDown(p);
            }
            else
            {
                rejector.OnUp(p);
            }
            var decision = rejector.ClassifyContact(p);
            results.Add(new
            {
                shouldDraw = rejector.ShouldDraw(p),
                shouldErase = rejector.ShouldErase(p),
                intent = (int)decision.Intent,
                eraserDiameter = decision.EraserDiameter
            });
        }
        return new
        {
            id,
            fn = "simulatePalm",
            input = new object[]
            {
                events.Select(e => new { type = e.Type, id = e.Id, kind = (int)e.Kind, contactWidth = e.W, contactHeight = e.H }).ToArray(),
                new
                {
                    palmContactThreshold = threshold,
                    penHasPriority = penPriority,
                    advanced = advanced is null ? null : AdvancedJson(advanced),
                    nibMode, forceEraser, forcePointEraser, pptGestureEligible = pptEligible
                }
            },
            expected = new
            {
                results,
                isPenActive = rejector.IsPenActive,
                activePalms = rejector.ActivePalms.OrderBy(x => x).ToArray()
            },
            tolerance = 1e-4
        };
    }

    private static void WriteReplayClock(string dir)
    {
        var cases = new List<object>
        {
            ClockCase("clock-basic", 1000, 1, new[] { "play", "advance:16", "advance:16" }),
            ClockCase("clock-speed-2", 1000, 2, new[] { "play", "advance:100" }),
            ClockCase("clock-complete", 100, 1, new[] { "play", "advance:150" }),
            ClockCase("clock-seek-clamp", 1000, 1, new[] { "seek:1500", "seek:-5" }),
            ClockCase("clock-toggle", 1000, 1, new[] { "toggle", "advance:100", "toggle", "advance:100", "toggle" }),
            ClockCase("clock-complete-replay", 100, 1, new[] { "play", "advance:100", "play" })
        };
        JsonWriter.Write(dir, "replay-clock.json", new
        {
            id = "replay-clock",
            entry = "harmony/core/src/main/ets/replay/ReplayClock.ts",
            cases
        });
    }

    private static object ClockCase(string id, double durationMs, double speed, string[] ops)
    {
        var clock = new ReplayClock(TimeSpan.FromMilliseconds(durationMs)) { Speed = (float)speed };
        foreach (var op in ops)
        {
            if (op == "play") clock.Play();
            else if (op == "pause") clock.Pause();
            else if (op == "toggle") clock.Toggle();
            else if (op.StartsWith("seek:")) clock.Seek(float.Parse(op.Substring(5)));
            else if (op.StartsWith("advance:")) clock.Advance(float.Parse(op.Substring(8)));
        }
        return new
        {
            id,
            fn = "simulateReplayClock",
            input = new object[] { durationMs, speed, ops },
            expected = new
            {
                elapsed = (double)clock.ElapsedMs,
                playing = clock.IsPlaying,
                complete = clock.IsComplete,
                progress = (double)clock.Progress
            },
            tolerance = 1e-4
        };
    }

    private static void WriteReplaySession(string dir)
    {
        var cases = new List<object>();
        AddSession(cases, "session-start", new[] { 3, 2, 1 }, 12, 0);
        AddSession(cases, "session-partial", new[] { 3, 2, 1 }, 12, 10);
        AddSession(cases, "session-boundary", new[] { 3, 2, 1 }, 12, 36);
        AddSession(cases, "session-later", new[] { 3, 2, 1 }, 12, 50);
        AddSession(cases, "session-end", new[] { 3, 2, 1 }, 12, 72);
        AddSession(cases, "session-done", new[] { 3, 2, 1 }, 12, 200);
        JsonWriter.Write(dir, "replay-session.json", new
        {
            id = "replay-session",
            entry = "harmony/core/src/main/ets/replay/InkReplaySession.ts",
            cases
        });
    }

    private static void AddSession(List<object> cases, string id, int[] pointCounts, float msPerPoint, float elapsed)
    {
        var strokes = pointCounts.Select(count =>
        {
            var stroke = new InkCanvas.Core.Ink.InkStroke(new InkCanvas.Core.Ink.StrokeAttributes());
            for (var i = 0; i < count; i++)
            {
                stroke.AddPoint(i, 0);
            }
            return stroke;
        }).ToList();
        var session = new InkReplaySession(strokes, msPerPoint);
        var visible = session.VisibleAt(elapsed);
        cases.Add(new
        {
            id,
            fn = "replayVisibleCounts",
            input = new object[] { pointCounts, msPerPoint, elapsed },
            expected = new
            {
                counts = visible.Select(s => s.Count).ToArray(),
                complete = session.IsComplete(elapsed)
            },
            tolerance = 0
        });
    }

    private static void WriteCompositing(string dir)
    {
        var cases = new List<object>();
        AddBlend(cases, "blend-opaque", 3, 2, new byte[] { 10, 20, 30, 255, 40, 50, 60, 255, 70, 80, 90, 255, 100, 110, 120, 255, 130, 140, 150, 255, 160, 170, 180, 255 },
            1, 0, 2, 2, new byte[] { 1, 2, 3, 255, 4, 5, 6, 255, 7, 8, 9, 255, 10, 11, 12, 255 });
        AddBlend(cases, "blend-half", 2, 2, new byte[] { 100, 100, 100, 255, 100, 100, 100, 255, 100, 100, 100, 255, 100, 100, 100, 255 },
            0, 0, 2, 2, new byte[] { 10, 20, 30, 128, 10, 20, 30, 128, 10, 20, 30, 128, 10, 20, 30, 128 });
        AddBlend(cases, "blend-transparent-src", 2, 1, new byte[] { 1, 2, 3, 255, 4, 5, 6, 255 },
            0, 0, 1, 1, new byte[] { 9, 9, 9, 0 });
        AddBlend(cases, "blend-clip-negative", 2, 2, new byte[] { 1, 1, 1, 255, 2, 2, 2, 255, 3, 3, 3, 255, 4, 4, 4, 255 },
            -1, -1, 2, 2, new byte[] { 10, 11, 12, 255, 20, 21, 22, 255, 30, 31, 32, 255, 40, 41, 42, 255 });
        JsonWriter.Write(dir, "compositing.json", new
        {
            id = "compositing",
            entry = "harmony/core/src/main/ets/imaging/PixelCompositing.ts",
            cases
        });
    }

    private static void AddBlend(List<object> cases, string id, int dw, int dh, byte[] dst, int dx, int dy,
        int sw, int sh, byte[] src)
    {
        var actual = dst.ToArray();
        PixelCompositing.BlendOver(actual, dw, dh, dx, dy, src, sw, sh);
        cases.Add(new
        {
            id,
            fn = "blendOver",
            input = new object[]
            {
                dst.Select(b => (int)b).ToArray(), dw, dh, dx, dy,
                src.Select(b => (int)b).ToArray(), sw, sh
            },
            expected = actual.Select(b => (int)b).ToArray(),
            tolerance = 0
        });
    }

    private static void WriteTheme(string dir)
    {
        var cases = new List<object>();
        foreach (var mode in new[] { 0, 1 })
        {
            foreach (var whiteboard in new[] { true, false })
            {
                foreach (var light in new[] { true, false })
                {
                    cases.Add(new
                    {
                        id = $"theme-{mode}-{whiteboard}-{light}",
                        fn = "getMainWindowTheme",
                        input = new object[] { mode, whiteboard, light },
                        expected = (int)ThemeResolver.GetMainWindowTheme(mode, whiteboard, light),
                        tolerance = 0
                    });
                }
            }
        }
        foreach (var whiteboard in new[] { true, false })
        {
            cases.Add(new
            {
                id = $"board-theme-{whiteboard}",
                fn = "getBoardTheme",
                input = new object[] { whiteboard },
                expected = (int)ThemeResolver.GetBoardTheme(whiteboard),
                tolerance = 0
            });
        }
        JsonWriter.Write(dir, "theme.json", new
        {
            id = "theme",
            entry = "harmony/core/src/main/ets/theming/ThemeResolver.ts",
            cases
        });
    }

    private static void WriteVersion(string dir)
    {
        var cases = new List<object>();
        var texts = new[] { "3.0.0", "v3.1.2", "V26.10.7", "1.2.3.4", " latest ", "3", "1.2.3.4.5", "-1.2", "3.x.0", "  v2.5  ", "" };
        foreach (var text in texts)
        {
            var ok = AppVersion.TryParse(text, out var version);
            cases.Add(new
            {
                id = "parse-" + (text.Length == 0 ? "empty" : text.Replace(" ", "_")),
                fn = "parseVersion",
                input = new object[] { text },
                expected = new
                {
                    ok,
                    major = ok ? version.Major : 0,
                    minor = ok ? version.Minor : 0,
                    patch = ok ? version.Patch : 0,
                    build = ok ? version.Build : 0,
                    formatted = ok ? version.ToString() : ""
                },
                tolerance = 0
            });
        }

        var pairs = new (int[], int[])[]
        {
            (new[] { 3, 0, 0, 0 }, new[] { 3, 0, 1, 0 }),
            (new[] { 3, 1, 0, 0 }, new[] { 3, 0, 9, 9 }),
            (new[] { 1, 2, 3, 4 }, new[] { 1, 2, 3, 4 }),
            (new[] { 0, 0, 0, 0 }, new[] { 0, 0, 0, 1 })
        };
        foreach (var (a, b) in pairs)
        {
            cases.Add(new
            {
                id = $"compare-{string.Join(".", a)}-{string.Join(".", b)}",
                fn = "compareVersions",
                input = new object[] { a, b },
                expected = Math.Sign(new AppVersion(a[0], a[1], a[2], a[3]).CompareTo(new AppVersion(b[0], b[1], b[2], b[3]))),
                tolerance = 0
            });
        }

        JsonWriter.Write(dir, "app-version.json", new
        {
            id = "app-version",
            entry = "harmony/core/src/main/ets/versioning/AppVersion.ts",
            cases
        });
    }

    private static void WriteUpdatePolicy(string dir)
    {
        var cases = new List<object>();
        var configs = new (bool Auto, bool Silent, string Start, string End)[]
        {
            (true, true, "00:00", "00:00"),
            (true, true, "18:20", "07:40"),
            (true, true, "09:00", "17:00"),
            (false, true, "00:00", "00:00"),
            (true, false, "00:00", "00:00"),
            (true, true, "bad", "07:40"),
            (true, true, "24:00", "07:40")
        };
        var times = new (int H, int M)[] { (0, 0), (3, 0), (7, 39), (7, 40), (12, 0), (18, 20), (23, 59), (9, 0), (17, 0) };
        foreach (var (auto, silent, start, end) in configs)
        {
            var settings = new StartupSettings
            {
                IsAutoUpdate = auto,
                IsAutoUpdateWithSilence = silent,
                AutoUpdateWithSilenceStartTime = start,
                AutoUpdateWithSilenceEndTime = end
            };
            foreach (var (h, m) in times)
            {
                var now = new DateTime(2026, 1, 1, h, m, 0);
                cases.Add(new
                {
                    id = $"policy-{auto}-{silent}-{start.Replace(":", "")}-{end.Replace(":", "")}-{h}-{m}",
                    fn = "mayInstallSilently",
                    input = new object[]
                    {
                        new { isAutoUpdate = auto, isAutoUpdateWithSilence = silent, startTime = start, endTime = end },
                        h, m
                    },
                    expected = UpdatePolicy.MayInstallSilently(settings, now),
                    tolerance = 0
                });
            }
        }
        JsonWriter.Write(dir, "update-policy.json", new
        {
            id = "update-policy",
            entry = "harmony/core/src/main/ets/update/UpdatePolicy.ts",
            cases
        });
    }

    private static void WriteReleaseFeed(string dir)
    {
        var latest = "{\"tag_name\":\"v3.5.0\",\"name\":\"3.5.0\",\"body\":\"notes\",\"html_url\":\"https://example/rel\",\"assets\":[{\"name\":\"InkCanvas-Setup-3.5.0.exe\",\"browser_download_url\":\"https://example/setup.exe\",\"size\":12345}]}";
        var fallback = "{\"tag_name\":\"3.4.1\",\"name\":\"3.4.1\",\"body\":\"b\",\"html_url\":\"https://example/rel2\",\"assets\":[{\"name\":\"other.exe\",\"browser_download_url\":\"https://example/other.exe\",\"size\":100},{\"name\":\"InkCanvas.exe\",\"browser_download_url\":\"https://example/app.exe\",\"size\":200}]}";
        var noExe = "{\"tag_name\":\"3.4.2\",\"name\":\"3.4.2\",\"assets\":[{\"name\":\"readme.txt\",\"browser_download_url\":\"https://example/readme.txt\"}]}";
        var list = "[{\"tag_name\":\"v9.0.0\",\"draft\":true},{\"tag_name\":\"v8.0.0\",\"prerelease\":true},{\"tag_name\":\"v3.1.0\",\"name\":\"a\",\"assets\":[{\"name\":\"setup.exe\",\"browser_download_url\":\"https://example/a.exe\",\"size\":1}]},{\"tag_name\":\"v3.2.0\",\"name\":\"b\",\"assets\":[{\"name\":\"setup.exe\",\"browser_download_url\":\"https://example/b.exe\",\"size\":2}]}]";

        var cases = new List<object>
        {
            FeedCase("latest-setup", "parseLatestSummary", latest),
            FeedCase("latest-fallback-exe", "parseLatestSummary", fallback),
            FeedCase("latest-no-exe", "parseLatestSummary", noExe),
            FeedCase("latest-invalid", "parseLatestSummary", "not json"),
            FeedCase("newest-stable", "parseNewestStableSummary", list),
            FeedCase("newest-invalid", "parseNewestStableSummary", "{}")
        };
        JsonWriter.Write(dir, "release-feed.json", new
        {
            id = "release-feed",
            entry = "harmony/core/src/main/ets/update/ReleaseFeed.ts",
            cases
        });
    }

    private static object FeedCase(string id, string fn, string json)
    {
        var info = fn.StartsWith("parseLatest") ? ReleaseFeed.ParseLatest(json) : ReleaseFeed.ParseNewestStable(json);
        object? expected = info is null
            ? null
            : new
            {
                Tag = info.Tag,
                Version = new[] { info.Version.Major, info.Version.Minor, info.Version.Patch, info.Version.Build },
                info.Name,
                info.Notes,
                info.PageUrl,
                info.InstallerUrl,
                info.InstallerSize
            };
        return new { id, fn, input = new object[] { json }, expected, tolerance = 0 };
    }
}
