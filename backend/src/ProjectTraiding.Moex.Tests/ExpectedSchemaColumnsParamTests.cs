using ProjectTraiding.Moex.Parsing;

namespace TestHistoryData
{
    public class ExpectedSchemaColumnsParamTests
    {
        [Fact]
        public void BuildColumnsParam_DataSchema_ReturnsColumnsInSchemaOrder()
        {
            string actual = ColumnAndNumbersForParsing.MegaAlertsAssetSchema.BuildColumnsParam();

            Assert.Equal(
                "tradedate,tradetime,secid,alert_type,threshold,value,reference,SYSTIME",
                actual);
        }

        [Fact]
        public void BuildColumnsParam_NonDataRootKey_ThrowsInvalidOperationException()
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                ColumnAndNumbersForParsing.AlgCandlesSchema.BuildColumnsParam);

            Assert.Contains("RootKey='candles'", exception.Message);
        }

        [Fact]
        public void BuildColumnsParam_DataSchemaWithSourceIndexGap_ThrowsInvalidOperationException()
        {
            ColumnAndNumbersForParsing.ExpectedSchema schema = new(
                TotalColumns: 3,
                Columns: new ColumnAndNumbersForParsing.ExpectedColumn[]
                {
                    new(0, "first"u8.ToArray()),
                    new(2, "third"u8.ToArray()),
                },
                RootKey: "data");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                schema.BuildColumnsParam);

            Assert.Contains("SourceIndex gaps", exception.Message);
        }
    }
}