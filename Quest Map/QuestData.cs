using System.Diagnostics.CodeAnalysis;
using Lumina;
using Lumina.Data;
using Lumina.Excel;
using Lumina.Text;

namespace QuestMap {
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    [SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
    internal class QuestData : ExcelRow {
        #pragma warning disable 8618
        public string Id { get; set; }
        public SeString Text { get; set; }
        #pragma warning restore 8618

        public override void PopulateData(RowParser parser, GameData gameData, Language language) {
            base.PopulateData(parser, gameData, language);

            this.Id = parser.ReadColumn<string>(0);
            this.Text = parser.ReadColumn<SeString>(1);
        }
    }
}
