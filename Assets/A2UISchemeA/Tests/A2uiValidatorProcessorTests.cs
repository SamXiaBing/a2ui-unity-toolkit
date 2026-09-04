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
}
