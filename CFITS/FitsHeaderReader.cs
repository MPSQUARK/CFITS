using System.Text;

namespace CFITS;

class FitsHeaderReader
{
	public static void ReadFitsHeader(string filePath)
	{
		byte[] buffer = new byte[Constants.BlockSize];
		bool endFound = false;

		using (FileStream fs = new(filePath, FileMode.Open, FileAccess.Read))
		{
			while (fs.Read(buffer, 0, Constants.BlockSize) == Constants.BlockSize && !endFound)
			{
				for (int i = 0; i < Constants.BlockSize; i += Constants.CardSize)
				{
					string card = Encoding.ASCII.GetString(buffer, i, Constants.CardSize);
					if (card.StartsWith("END"))
					{
						endFound = true;
						break;
					}

					string keyword = card[..8].Trim();
					string value = card.Length > 10 ? card[10..].Trim() : string.Empty;

					// Optionally, separate the value from the comment if present.
					// For simplicity, this example just prints the keyword and value/comment part.
					Console.WriteLine($"Keyword: {keyword}, Value/Comment: {value}");
				}
			}
		}
	}
}