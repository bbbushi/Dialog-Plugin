using NUnit.Framework;
using UnityEngine;

namespace Dialogue.Tests.EditMode
{
    /// <summary>
    /// DialogueVariables 存储 + DialogueExpression 解析的 EditMode 单元测试。
    /// 纯普通类，无需 SO/SerializedObject。
    /// </summary>
    public class DialogueVariableTests
    {
        // ---------- Evaluate：空值与字面量 ----------

        [Test]
        public void Evaluate_EmptyOrNull_ReturnsTrue()
        {
            var vars = new DialogueVariables();
            Assert.IsTrue(DialogueExpression.Evaluate(vars, null));
            Assert.IsTrue(DialogueExpression.Evaluate(vars, ""));
            Assert.IsTrue(DialogueExpression.Evaluate(vars, "   "));
        }

        [Test]
        public void Evaluate_ComparisonOps_Work()
        {
            var vars = new DialogueVariables();
            vars.Set("courage", 3);

            Assert.IsTrue(DialogueExpression.Evaluate(vars, "courage==3"));
            Assert.IsTrue(DialogueExpression.Evaluate(vars, "courage!=4"));
            Assert.IsTrue(DialogueExpression.Evaluate(vars, "courage>=3"));
            Assert.IsTrue(DialogueExpression.Evaluate(vars, "courage<=3"));
            Assert.IsTrue(DialogueExpression.Evaluate(vars, "courage>2"));
            Assert.IsTrue(DialogueExpression.Evaluate(vars, "courage<4"));

            Assert.IsFalse(DialogueExpression.Evaluate(vars, "courage==4"));
            Assert.IsFalse(DialogueExpression.Evaluate(vars, "courage>3"));
        }

        [Test]
        public void Evaluate_BoolLiterals_ParsedAs01()
        {
            var vars = new DialogueVariables();
            vars.Set("flag", 1);
            Assert.IsTrue(DialogueExpression.Evaluate(vars, "flag==true"));

            vars.Set("flag", 0);
            Assert.IsTrue(DialogueExpression.Evaluate(vars, "flag==false"));
        }

        [Test]
        public void Evaluate_UndefinedVar_ReadsAsZero()
        {
            var vars = new DialogueVariables();
            Assert.IsFalse(DialogueExpression.Evaluate(vars, "courage>=1"));
            Assert.IsTrue(DialogueExpression.Evaluate(vars, "courage>=0"));
        }

        // ---------- Evaluate：语法错误 ----------

        [Test]
        public void Evaluate_MissingOperator_Throws()
        {
            var vars = new DialogueVariables();
            Assert.Throws<System.FormatException>(() => DialogueExpression.Evaluate(vars, "courage 3"));
        }

        [Test]
        public void Evaluate_BadValue_Throws()
        {
            var vars = new DialogueVariables();
            Assert.Throws<System.FormatException>(() => DialogueExpression.Evaluate(vars, "courage>=abc"));
        }

        [Test]
        public void Evaluate_MissingOperand_Throws()
        {
            var vars = new DialogueVariables();
            Assert.Throws<System.FormatException>(() => DialogueExpression.Evaluate(vars, ">=3"));
        }

        // ---------- Apply：赋值 ----------

        [Test]
        public void Apply_AssignAddSub_Work()
        {
            var vars = new DialogueVariables();
            DialogueExpression.Apply(vars, "courage=5");
            Assert.AreEqual(5, vars.Get("courage"));

            DialogueExpression.Apply(vars, "courage+3");
            Assert.AreEqual(8, vars.Get("courage"));

            DialogueExpression.Apply(vars, "courage-2");
            Assert.AreEqual(6, vars.Get("courage"));
        }

        [Test]
        public void Apply_Multiple_SemicolonSeparated()
        {
            var vars = new DialogueVariables();
            DialogueExpression.Apply(vars, "courage+1; met=true;  ");
            Assert.AreEqual(1, vars.Get("courage"));
            Assert.AreEqual(1, vars.Get("met"));
        }

        [Test]
        public void Apply_BadExpression_Throws()
        {
            var vars = new DialogueVariables();
            Assert.Throws<System.FormatException>(() => DialogueExpression.Apply(vars, "courage*2"));
        }

        [Test]
        public void Apply_EmptyOrNull_NoOp()
        {
            var vars = new DialogueVariables();
            Assert.DoesNotThrow(() => DialogueExpression.Apply(vars, null));
            Assert.DoesNotThrow(() => DialogueExpression.Apply(vars, "  "));
        }

        // ---------- Variables：事件与导入 ----------

        [Test]
        public void Variables_SetSameValue_NoEvent()
        {
            var vars = new DialogueVariables();
            int fired = 0;
            vars.Changed += () => fired++;

            vars.Set("a", 1);
            Assert.AreEqual(1, fired);

            vars.Set("a", 1); // 同值不触发
            Assert.AreEqual(1, fired);

            vars.Set("a", 2);
            Assert.AreEqual(2, fired);
        }

        [Test]
        public void Variables_Import_Replaces()
        {
            var vars = new DialogueVariables();
            vars.Set("a", 1);
            vars.Set("b", 2);

            var src = new System.Collections.Generic.Dictionary<string, int> { { "c", 9 } };
            vars.Import(src);

            Assert.AreEqual(0, vars.Get("a")); // 旧键被清
            Assert.AreEqual(0, vars.Get("b"));
            Assert.AreEqual(9, vars.Get("c"));
        }
    }
}
