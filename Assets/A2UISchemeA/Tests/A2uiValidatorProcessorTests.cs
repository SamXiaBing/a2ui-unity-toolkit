using System;
using System.Text;
using NUnit.Framework;

namespace A2UISchemeA.Tests
{
    /// <summary>G0 校验门禁：坏包必须拒收，好包必须放行。</summary>
    [TestFixture]
    public class A2uiValidatorTests
    {
        [Test]
        public void Valid_TwoMessages_Pass()
        {
            var v = A2uiV08Validator.ValidateJsonl(
                "{\"surfaceUpdate\":{\"surfaceId\":\"s\",\"components\":[" +
                "{\"id\":\"r\",\"component\":{\"Text\":{\"text\":{\"literalString\":\"hi\"}}}}]}}\n" +
                "{\"beginRendering\":{\"surfaceId\":\"s\",\"root\":\"r\",\"catalogId\":\"demo\"}}",
                out _);
            Assert.IsTrue(v.Ok);
        }

        [Test]
        public void MissingComponents_Rejected()
        {
            // 历史样本 invalid_bad_packet：surfaceUpdate 无 components 数组
            var v = A2uiV08Validator.ValidateJsonl(
                "{\"surfaceUpdate\":{\"surfaceId\":\"broken\"}}\n" +
                "{\"beginRendering\":{\"surfaceId\":\"broken\",\"root\":\"missing\"}}",
                out _);
            Assert.IsFalse(v.Ok, "缺 components 必须拒收");
        }

        [Test]
        public void EmptyPayload_Rejected()
        {
            var v = A2uiV08Validator.ValidateJsonl("", out _);
            Assert.IsFalse(v.Ok);
        }

        [Test]
        public void MultiKeyMessage_SplitAccepted()
        {
            // 校验器对多键行是「容错拆分」策略（为 LLM 合并输出兜底）：
            // 一行含 surfaceUpdate + beginRendering 应拆成 2 条消息整体放行
            var v = A2uiV08Validator.ValidateJsonl(
                "{\"surfaceUpdate\":{\"surfaceId\":\"s\",\"components\":[]},\"beginRendering\":{\"surfaceId\":\"s\",\"root\":\"r\"}}",
                out var msgs);
            Assert.IsTrue(v.Ok, "多键行应拆分放行");
            Assert.AreEqual(2, msgs.Count, "拆成 2 条单类型消息");
        }

        [Test]
        public void UnknownMessageType_Rejected()
        {
            var v = A2uiV08Validator.ValidateJsonl("{\"someUpdate\":{\"a\":1}}", out _);
            Assert.IsFalse(v.Ok);
        }
    }

    /// <summary>Processor 生命周期与数据绑定。</summary>
    [TestFixture]
    public class A2uiProcessorTests
    {
        [Test]
        public void BeginRendering_MakesSurfaceReady()
        {
            var p = new A2uiV08Processor();
            p.IngestMessage(Parse("{\"surfaceUpdate\":{\"surfaceId\":\"s\",\"components\":[" +
                "{\"id\":\"r\",\"component\":{\"Text\":{\"text\":{\"literalString\":\"x\"}}}}]}}"));
            Assert.IsFalse(p.Surfaces["s"].ReadyToRender, "beginRendering 前不可渲染");
            p.IngestMessage(Parse("{\"beginRendering\":{\"surfaceId\":\"s\",\"root\":\"r\",\"catalogId\":\"demo\"}}"));
            Assert.IsTrue(p.Surfaces["s"].ReadyToRender);
        }

        [Test]
        public void DeleteSurface_RemovesAndFires()
        {
            var p = new A2uiV08Processor();
            string deleted = null;
            p.SurfaceDeleted += id => deleted = id;
            p.IngestMessage(Parse("{\"surfaceUpdate\":{\"surfaceId\":\"s\",\"components\":[]}}"));
            p.IngestMessage(Parse("{\"deleteSurface\":{\"surfaceId\":\"s\"}}"));
            Assert.AreEqual("s", deleted);
            Assert.IsFalse(p.Surfaces.ContainsKey("s"));
        }

        [Test]
        public void DataModelUpdate_PatchesByPath()
        {
            var p = new A2uiV08Processor();
            p.IngestMessage(Parse("{\"surfaceUpdate\":{\"surfaceId\":\"s\",\"components\":[]}}"));
            p.IngestMessage(Parse("{\"dataModelUpdate\":{\"surfaceId\":\"s\",\"path\":\"/media\"," +
                "\"contents\":[{\"key\":\"title\",\"valueString\":\"夜航星图\"}]}}"));
            var v = A2uiV08Processor.GetByPath(p.Surfaces["s"].DataModel, "/media/title");
            Assert.AreEqual("夜航星图", (string)v);
        }

        [Test]
        public void GetByPath_ArrayIndex_Works()
        {
            var root = new Newtonsoft.Json.Linq.JObject
            {
                ["list"] = new Newtonsoft.Json.Linq.JArray("a", "b", "c")
            };
            Assert.AreEqual("b", (string)A2uiV08Processor.GetByPath(root, "/list/1"));
            Assert.IsNull(A2uiV08Processor.GetByPath(root, "/list/9"), "越界下标返回 null 不抛异常");
        }

        static Newtonsoft.Json.Linq.JObject Parse(string s) => Newtonsoft.Json.Linq.JObject.Parse(s);
    }

    /// <summary>v0.9 双栈：平铺消息格式 → 归一化 → 内部模型。</summary>
    [TestFixture]
    public class A2uiV09DualStackTests
    {
        [Test]
        public void IsV09_DetectsCreateSurface()
        {
            var msg = Newtonsoft.Json.Linq.JObject.Parse(
                "{\"createSurface\":{\"surfaceId\":\"s\",\"catalogId\":\"c\"}}");
            Assert.IsTrue(A2uiV09Normalizer.IsV09(msg));
        }

        [Test]
        public void IsV09_RejectsV08()
        {
            var msg = Newtonsoft.Json.Linq.JObject.Parse(
                "{\"surfaceUpdate\":{\"surfaceId\":\"s\",\"components\":[]}}");
            Assert.IsFalse(A2uiV09Normalizer.IsV09(msg));
        }

        [Test]
        public void Normalize_CreateSurface_ProducesSuAndBr()
        {
            var msg = Newtonsoft.Json.Linq.JObject.Parse(
                "{\"createSurface\":{\"surfaceId\":\"s\",\"catalogId\":\"https://x/1\"}}");
            var result = A2uiV09Normalizer.Normalize(msg);
            Assert.AreEqual(2, result.Count, "createSurface 应产出 surfaceUpdate + beginRendering");
            Assert.IsNotNull(result[0]["surfaceUpdate"], "第一条是 surfaceUpdate");
            Assert.IsNotNull(result[1]["beginRendering"], "第二条是 beginRendering");
            Assert.AreEqual("s", (string)result[0]["surfaceUpdate"]["surfaceId"]);
        }

        [Test]
        public void Normalize_UpdateComponents_FlatToNested()
        {
            // v0.9 平铺：component 是字符串，text 是直接值，children 是数组
            var msg = Newtonsoft.Json.Linq.JObject.Parse(
                "{\"updateComponents\":{\"surfaceId\":\"s\",\"components\":[" +
                "  {\"id\":\"root\",\"component\":\"Column\",\"children\":[\"t1\",\"t2\"]}," +
                "  {\"id\":\"t1\",\"component\":\"Text\",\"text\":\"Hello\",\"variant\":\"h2\"}," +
                "  {\"id\":\"t2\",\"component\":\"Button\",\"child\":\"btn_t\",\"variant\":\"primary\",\"action\":{\"event\":{\"name\":\"go\"}}}," +
                "  {\"id\":\"btn_t\",\"component\":\"Text\",\"text\":\"Go\",\"variant\":\"body\"}" +
                "]}}");
            var result = A2uiV09Normalizer.Normalize(msg);
            Assert.AreEqual(1, result.Count);
            var su = result[0]["surfaceUpdate"];
            Assert.IsNotNull(su);
            var comps = (Newtonsoft.Json.Linq.JArray)su["components"];
            Assert.AreEqual(4, comps.Count);

            // root Column：children 数组 → explicitList
            var root = (Newtonsoft.Json.Linq.JObject)comps[0];
            var rootWrapper = root["component"] as Newtonsoft.Json.Linq.JObject;
            Assert.IsNotNull(rootWrapper, "component 应为嵌套 wrapper");
            Assert.IsNotNull(rootWrapper["Column"], "component wrapper 内应有 Column key");
            Assert.IsNotNull(rootWrapper["Column"]["children"]["explicitList"],
                "children 数组归一化为 explicitList");

            // Text t1：text 字面值 → literalString 包装，variant → usageHint
            var t1 = (Newtonsoft.Json.Linq.JObject)comps[1];
            var t1Wrapper = t1["component"] as Newtonsoft.Json.Linq.JObject;
            Assert.IsNotNull(t1Wrapper["Text"], "Text wrapper 应存在");
            // text 属性在外面（Mapper ResolveString 消费外层）
            Assert.IsNotNull(t1["text"], "text 属性在顶层供 Mapper 消费");
        }

        [Test]
        public void Normalize_UpdateDataModel_ValueToContents()
        {
            var msg = Newtonsoft.Json.Linq.JObject.Parse(
                "{\"updateDataModel\":{\"surfaceId\":\"s\",\"path\":\"/battery\",\"value\":{\"level\":18}}}");
            var result = A2uiV09Normalizer.Normalize(msg);
            Assert.AreEqual(1, result.Count);
            var dm = result[0]["dataModelUpdate"];
            Assert.IsNotNull(dm);
            Assert.AreEqual("s", (string)dm["surfaceId"]);
            Assert.IsNotNull(dm["contents"], "v0.9 value 归一化为 v0.8 contents 数组");
        }

        [Test]
        public void Normalize_DeleteSurface_Passthrough()
        {
            var msg = Newtonsoft.Json.Linq.JObject.Parse(
                "{\"deleteSurface\":{\"surfaceId\":\"s\"}}");
            var result = A2uiV09Normalizer.Normalize(msg);
            Assert.AreEqual(1, result.Count);
            Assert.IsNotNull(result[0]["deleteSurface"]);
        }

        [Test]
        public void Processor_IngestV09Jsonl_Renders()
        {
            var jsonl = "{\"createSurface\":{\"surfaceId\":\"s9\",\"catalogId\":\"test\"}}\n" +
                "{\"updateComponents\":{\"surfaceId\":\"s9\",\"components\":[" +
                "{\"id\":\"root\",\"component\":\"Column\",\"children\":[\"t\"]}," +
                "{\"id\":\"t\",\"component\":\"Text\",\"text\":\"v0.9 works\"}" +
                "]}}";
            var v = A2uiV08Validator.ValidateJsonl(jsonl, out var msgs);
            Assert.IsTrue(v.Ok, "v0.9 JSONL 应通过校验（归一化后）：如果失败: " + v.Error);

            var p = new A2uiV08Processor();
            foreach (var m in msgs) p.IngestMessage(m);
            Assert.IsTrue(p.Surfaces.ContainsKey("s9"));
            Assert.IsTrue(p.Surfaces["s9"].ReadyToRender, "v0.9 createSurface 应标记 ready");
            Assert.AreEqual("root", p.Surfaces["s9"].RootId);
            Assert.IsTrue(p.Surfaces["s9"].Components.ContainsKey("root"), "归一化组件应注册到 state");
        }

        [Test]
        public void Validator_V09_ValidPasses()
        {
            var v = A2uiV08Validator.ValidateJsonl(
                "{\"createSurface\":{\"surfaceId\":\"s\",\"catalogId\":\"c\"}}", out _);
            Assert.IsTrue(v.Ok);
        }

        [Test]
        public void Validator_V09_MissingCatalogId_Fails()
        {
            var v = A2uiV08Validator.ValidateJsonl(
                "{\"createSurface\":{\"surfaceId\":\"s\"}}", out _);
            Assert.IsFalse(v.Ok, "缺 catalogId 必须拒收");
        }
    }

    /// <summary>
    /// 规模上限防线（对齐 Compose 参考渲染器 1MB/1000/50，属加固项而非协议要求）：
    /// 超大载荷、超量组件必须整体拒收保留上一帧；surface 名额超限抛错由宿主按保留上一帧处理。
    /// </summary>
    [TestFixture]
    public class A2uiSafetyCapTests
    {
        [Test]
        public void PayloadOver1MB_Rejected()
        {
            // 'a' 在 UTF-8 下 1 字节/字符：1_100_000 字符即 1.1MB，超上限
            var big = new string('a', 1_100_000);
            var jsonl = "{\"dataModelUpdate\":{\"surfaceId\":\"s\",\"contents\":[" +
                        "{\"key\":\"blob\",\"valueString\":\"" + big + "\"}]}}";
            var v = A2uiV08Validator.ValidateJsonl(jsonl, out _);
            Assert.IsFalse(v.Ok, "超过 1MB 的载荷必须拒收");
            StringAssert.Contains("max message size", v.Error);
        }

        [Test]
        public void PayloadUnder1MB_Passes()
        {
            var big = new string('a', 900_000);
            var jsonl = "{\"dataModelUpdate\":{\"surfaceId\":\"s\",\"contents\":[" +
                        "{\"key\":\"blob\",\"valueString\":\"" + big + "\"}]}}";
            var v = A2uiV08Validator.ValidateJsonl(jsonl, out _);
            Assert.IsTrue(v.Ok, "1MB 以内的合法载荷应放行: " + v.Error);
        }

        [Test]
        public void V08_ComponentsOver1000_Rejected()
        {
            var jsonl = "{\"surfaceUpdate\":{\"surfaceId\":\"s\",\"components\":[" +
                        BuildV08Components(1001) + "]}}";
            var v = A2uiV08Validator.ValidateJsonl(jsonl, out _);
            Assert.IsFalse(v.Ok, "单条 surfaceUpdate 超 1000 组件必须拒收");
            StringAssert.Contains("max component count", v.Error);
        }

        [Test]
        public void V08_ComponentsExactly1000_Passes()
        {
            var jsonl = "{\"surfaceUpdate\":{\"surfaceId\":\"s\",\"components\":[" +
                        BuildV08Components(1000) + "]}}";
            var v = A2uiV08Validator.ValidateJsonl(jsonl, out _);
            Assert.IsTrue(v.Ok, "边界值 1000 组件应放行: " + v.Error);
        }

        [Test]
        public void V09_UpdateComponentsOver1000_Rejected()
        {
            var jsonl = "{\"updateComponents\":{\"surfaceId\":\"s\",\"components\":[" +
                        BuildV09Components(1001) + "]}}";
            var v = A2uiV08Validator.ValidateJsonl(jsonl, out _);
            Assert.IsFalse(v.Ok, "单条 updateComponents 超 1000 组件必须拒收");
            StringAssert.Contains("max component count", v.Error);
        }

        [Test]
        public void Processor_SurfaceCountOver50_ThrowsAndDeleteFreesSlot()
        {
            var p = new A2uiV08Processor();
            for (var i = 0; i < A2uiV08Processor.MaxSurfaces; i++)
                p.IngestMessage(Parse("{\"surfaceUpdate\":{\"surfaceId\":\"s" + i + "\",\"components\":[]}}"));
            Assert.Throws<InvalidOperationException>(() =>
                p.IngestMessage(Parse("{\"surfaceUpdate\":{\"surfaceId\":\"overflow\",\"components\":[]}}")),
                "第 51 个 surface 必须被拒绝");
            Assert.AreEqual(A2uiV08Processor.MaxSurfaces, p.Surfaces.Count);

            // 删一个释放名额后，可再建
            p.IngestMessage(Parse("{\"deleteSurface\":{\"surfaceId\":\"s0\"}}"));
            p.IngestMessage(Parse("{\"surfaceUpdate\":{\"surfaceId\":\"overflow\",\"components\":[]}}"));
            Assert.IsTrue(p.Surfaces.ContainsKey("overflow"), "释放名额后应可再建 surface");
        }

        static string BuildV08Components(int n)
        {
            var items = new string[n];
            for (var i = 0; i < n; i++)
                items[i] = "{\"id\":\"c" + i + "\",\"component\":{\"Text\":{\"text\":{\"literalString\":\"x\"}}}}";
            return string.Join(",", items);
        }

        static string BuildV09Components(int n)
        {
            var items = new string[n];
            for (var i = 0; i < n; i++)
                items[i] = "{\"id\":\"c" + i + "\",\"component\":\"Text\",\"text\":\"x\"}";
            return string.Join(",", items);
        }

        static Newtonsoft.Json.Linq.JObject Parse(string s) => Newtonsoft.Json.Linq.JObject.Parse(s);
    }

    /// <summary>D2：数据模型防护上限（Path 深度 10 / 键长 50 / 错误条数 100）。</summary>
    [TestFixture]
    public class A2uiDataModelCapTests
    {
        [Test]
        public void V08_PathDepth10_Passes_11_Rejected()
        {
            var p10 = "/" + string.Join("/", new[] { "a", "b", "c", "d", "e", "f", "g", "h", "i", "j" });
            var ok = A2uiV08Validator.ValidateJsonl(
                "{\"dataModelUpdate\":{\"surfaceId\":\"s\",\"path\":\"" + p10 +
                "\",\"contents\":[{\"key\":\"k\",\"valueString\":\"v\"}]}}", out _);
            Assert.IsTrue(ok.Ok, "path 恰好 10 段应放行: " + ok.Error);

            var p11 = p10 + "/k";
            var bad = A2uiV08Validator.ValidateJsonl(
                "{\"dataModelUpdate\":{\"surfaceId\":\"s\",\"path\":\"" + p11 +
                "\",\"contents\":[{\"key\":\"k\",\"valueString\":\"v\"}]}}", out _);
            Assert.IsFalse(bad.Ok, "path 11 段必须拒收");
            StringAssert.Contains("max path depth", bad.Error);
            Assert.AreEqual("/dataModelUpdate/path", bad.Path, "错误 Path 必须是 JSON Pointer");
        }

        [Test]
        public void V09_PathOverDepth_Rejected()
        {
            var p11 = "/" + string.Join("/", new[] { "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k" });
            var bad = A2uiV08Validator.ValidateJsonl(
                "{\"updateDataModel\":{\"surfaceId\":\"s\",\"path\":\"" + p11 + "\",\"value\":1}}", out _);
            Assert.IsFalse(bad.Ok);
            StringAssert.Contains("max path depth", bad.Error);
            Assert.AreEqual("/updateDataModel/path", bad.Path);
        }

        [Test]
        public void V08_KeyLength50_Passes_51_Rejected()
        {
            var k50 = new string('k', 50);
            var ok = A2uiV08Validator.ValidateJsonl(
                "{\"dataModelUpdate\":{\"surfaceId\":\"s\",\"contents\":[" +
                "{\"key\":\"" + k50 + "\",\"valueString\":\"v\"}]}}", out _);
            Assert.IsTrue(ok.Ok, "键长恰好 50 应放行: " + ok.Error);

            var k51 = new string('k', 51);
            var bad = A2uiV08Validator.ValidateJsonl(
                "{\"dataModelUpdate\":{\"surfaceId\":\"s\",\"contents\":[" +
                "{\"key\":\"" + k51 + "\",\"valueString\":\"v\"}]}}", out _);
            Assert.IsFalse(bad.Ok, "键长 51 必须拒收");
            StringAssert.Contains("max key length", bad.Error);
            Assert.AreEqual("/dataModelUpdate/contents/0/key", bad.Path);
        }

        [Test]
        public void V09_ValueKeyLength51_Rejected()
        {
            var k51 = new string('k', 51);
            var bad = A2uiV08Validator.ValidateJsonl(
                "{\"updateDataModel\":{\"surfaceId\":\"s\",\"value\":{\"" + k51 + "\":1}}}", out _);
            Assert.IsFalse(bad.Ok);
            StringAssert.Contains("max key length", bad.Error);
        }

        [Test]
        public void V08_ValueNestingBoundary()
        {
            // contents 数据键包在 key/valueMap 壳里：嵌套 k 层 valueMap = 数据键在第 k+1 层，
            // 故 9 层 valueMap（最深键第 10 层）放行，10 层（第 11 层）拒收。
            var ok = A2uiV08Validator.ValidateJsonl(
                "{\"dataModelUpdate\":{\"surfaceId\":\"s\",\"contents\":[" + NestedValueMap(9) + "]}}", out _);
            Assert.IsTrue(ok.Ok, "嵌套 9 层 valueMap（键最深第 10 层）应放行: " + ok.Error);

            var bad = A2uiV08Validator.ValidateJsonl(
                "{\"dataModelUpdate\":{\"surfaceId\":\"s\",\"contents\":[" + NestedValueMap(10) + "]}}", out _);
            Assert.IsFalse(bad.Ok, "嵌套 10 层 valueMap（键最深第 11 层）必须拒收");
            StringAssert.Contains("max path depth", bad.Error);
        }

        [Test]
        public void V09_ValueNestingBoundary()
        {
            var ok = A2uiV08Validator.ValidateJsonl(
                "{\"updateDataModel\":{\"surfaceId\":\"s\",\"value\":" + NestedObject(10) + "}}", out _);
            Assert.IsTrue(ok.Ok, "值嵌套恰好 10 层应放行: " + ok.Error);

            var bad = A2uiV08Validator.ValidateJsonl(
                "{\"updateDataModel\":{\"surfaceId\":\"s\",\"value\":" + NestedObject(11) + "}}", out _);
            Assert.IsFalse(bad.Ok, "值嵌套 11 层必须拒收");
            StringAssert.Contains("max path depth", bad.Error);
        }

        [Test]
        public void Errors_CappedAt100()
        {
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < 120; i++)
                sb.Append("{\"nope\":{}}\n");
            var v = A2uiV08Validator.ValidateJsonl(sb.ToString(), out _);
            Assert.IsFalse(v.Ok);
            StringAssert.Contains("truncated at 100", v.Error, "错误条数应在 100 处截断");
        }

        /// <summary>构造嵌套 k 层 valueMap 的 contents entry（每层 {"key":"a","valueMap":[…]}）。</summary>
        static string NestedValueMap(int depth)
        {
            var inner = "{\"key\":\"leaf\",\"valueString\":\"x\"}";
            for (var i = 0; i < depth; i++)
                inner = "{\"key\":\"a\",\"valueMap\":[" + inner + "]}";
            return inner;
        }

        /// <summary>构造嵌套 depth 层的裸对象 {"a":{"a":…}}。</summary>
        static string NestedObject(int depth)
        {
            var inner = "1";
            for (var i = 0; i < depth; i++)
                inner = "{\"a\":" + inner + "}";
            return inner;
        }
    }

    /// <summary>D2：surfaceId 会话内唯一——重复 createSurface 未删先建拒绝。</summary>
    [TestFixture]
    public class A2uiSurfaceIdUniquenessTests
    {
        [Test]
        public void DuplicateCreate_InSamePayload_Rejected()
        {
            var jsonl = "{\"createSurface\":{\"surfaceId\":\"s\",\"catalogId\":\"c\"}}\n" +
                        "{\"createSurface\":{\"surfaceId\":\"s\",\"catalogId\":\"c\"}}";
            var v = A2uiV08Validator.ValidateJsonl(jsonl, out _);
            Assert.IsFalse(v.Ok, "同载荷内重复 createSurface 必须拒收");
            StringAssert.Contains("duplicate createSurface", v.Error);
            Assert.AreEqual("/createSurface/surfaceId", v.Path);
        }

        [Test]
        public void CreateDeleteCreate_SamePayload_Passes()
        {
            var jsonl = "{\"createSurface\":{\"surfaceId\":\"s\",\"catalogId\":\"c\"}}\n" +
                        "{\"deleteSurface\":{\"surfaceId\":\"s\"}}\n" +
                        "{\"createSurface\":{\"surfaceId\":\"s\",\"catalogId\":\"c\"}}";
            var v = A2uiV08Validator.ValidateJsonl(jsonl, out _);
            Assert.IsTrue(v.Ok, "删除后重建应放行: " + v.Error);
        }

        [Test]
        public void Processor_DuplicateCreate_Throws_DeleteFreesId()
        {
            var p = new A2uiV08Processor();
            p.IngestMessage(Parse("{\"createSurface\":{\"surfaceId\":\"s\",\"catalogId\":\"c\"}}"));
            Assert.Throws<InvalidOperationException>(() =>
                p.IngestMessage(Parse("{\"createSurface\":{\"surfaceId\":\"s\",\"catalogId\":\"c\"}}")),
                "跨载荷重复 createSurface（未删先建）必须拒绝");
            Assert.IsTrue(p.Surfaces.ContainsKey("s"), "拒收时保留既有 surface（不回滚）");

            p.IngestMessage(Parse("{\"deleteSurface\":{\"surfaceId\":\"s\"}}"));
            Assert.DoesNotThrow(() =>
                p.IngestMessage(Parse("{\"createSurface\":{\"surfaceId\":\"s\",\"catalogId\":\"c\"}}")),
                "删除后重建应放行");
        }

        [Test]
        public void Processor_V08_SurfaceUpdate_RemainsIdempotent()
        {
            // v0.8 无 createSurface 概念（surface 隐式），重复 surfaceUpdate 是合法增量，不受唯一性判定影响
            var p = new A2uiV08Processor();
            Assert.DoesNotThrow(() =>
            {
                p.IngestMessage(Parse("{\"surfaceUpdate\":{\"surfaceId\":\"s\",\"components\":[]}}"));
                p.IngestMessage(Parse("{\"surfaceUpdate\":{\"surfaceId\":\"s\",\"components\":[]}}"));
            });
        }

        static Newtonsoft.Json.Linq.JObject Parse(string s) => Newtonsoft.Json.Linq.JObject.Parse(s);
    }

    /// <summary>D3：官方形态 error 封套（顶层 {version, error:{code,surfaceId,message,path}}，path 用 JSON Pointer）。</summary>
    [TestFixture]
    public class A2uiErrorEnvelopeTests
    {
        [Test]
        public void Build_HasOfficialShape()
        {
            var env = A2uiErrorEnvelope.Build("VALIDATION_FAILED", "user_profile_card",
                "Expected stringOrPath, got integer", "/components/0/text");
            Assert.AreEqual("v0.9", (string)env["version"]);
            var e = env["error"];
            Assert.AreEqual("VALIDATION_FAILED", (string)e["code"]);
            Assert.AreEqual("user_profile_card", (string)e["surfaceId"]);
            Assert.AreEqual("/components/0/text", (string)e["path"]);
            Assert.AreEqual("Expected stringOrPath, got integer", (string)e["message"]);
        }

        [Test]
        public void Build_FillsRequiredDefaults()
        {
            var env = A2uiErrorEnvelope.Build(null, null, null, null);
            var e = env["error"];
            Assert.AreEqual("VALIDATION_FAILED", (string)e["code"], "空 code 回落官方校验错误码");
            Assert.AreEqual("", (string)e["surfaceId"], "未知 surfaceId 落空串（字段必填）");
            Assert.AreEqual("/", (string)e["path"], "未知 path 落根指针（字段必填）");
        }

        [Test]
        public void TryExtractSurfaceId_V09AndV08()
        {
            Assert.AreEqual("s9", A2uiErrorEnvelope.TryExtractSurfaceId(
                "{\"version\":\"v0.9\",\"createSurface\":{\"surfaceId\":\"s9\",\"catalogId\":\"c\"}}"));
            Assert.AreEqual("s8", A2uiErrorEnvelope.TryExtractSurfaceId(
                "{\"surfaceUpdate\":{\"surfaceId\":\"s8\",\"components\":[]}}"));
            Assert.IsNull(A2uiErrorEnvelope.TryExtractSurfaceId("not json at all"));
            Assert.IsNull(A2uiErrorEnvelope.TryExtractSurfaceId(null));
        }

        [Test]
        public void ValidationFailure_PathIsJsonPointer()
        {
            var v = A2uiV08Validator.ValidateJsonl(
                "{\"surfaceUpdate\":{\"surfaceId\":\"s\",\"components\":[" +
                "{\"id\":\"r\",\"component\":{\"Text\":{},\"Row\":{}}}]}}", out _);
            Assert.IsFalse(v.Ok);
            Assert.AreEqual("/surfaceUpdate/components/0/component", v.Path,
                "校验失败必须带指向字段位置的 JSON Pointer");
        }

        [Test]
        public void Recorder_RecordError_WritesJsonl()
        {
            var rec = new A2uiSessionRecorder();
            rec.Begin("err_env_test");
            rec.RecordError(A2uiErrorEnvelope.Build("VALIDATION_FAILED", "s", "m", "/x"));

            var sessionPath = rec.ExportPath();
            var session = System.IO.File.ReadAllText(sessionPath);
            StringAssert.Contains("\"error\"", session, "session 事件流应含 error 事件");

            var errorsPath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(sessionPath),
                "errors_err_env_test.jsonl");
            // append 语义：先清掉上次运行的残留，保证本断言只看本次写入
            if (System.IO.File.Exists(errorsPath)) System.IO.File.Delete(errorsPath);
            rec.RecordError(A2uiErrorEnvelope.Build("VALIDATION_FAILED", "s", "m", "/x"));
            Assert.IsTrue(System.IO.File.Exists(errorsPath), "errors JSONL 应落盘");
            var line = System.IO.File.ReadAllText(errorsPath).Trim();
            var parsed = Newtonsoft.Json.Linq.JObject.Parse(line);
            Assert.AreEqual("VALIDATION_FAILED", (string)parsed["error"]["code"],
                "每行一个官方封套，agent 侧可直接解析");
        }
    }
}
