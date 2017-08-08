using System.Windows.Forms;

namespace Microscope_Control
{
    class PromptDialog
    {
        public static string ShowDialog(string text, string caption)
        {
            Form prompt = new Form()
            {
                Width = 500,
                Height = 100,
                FormBorderStyle = FormBorderStyle.None,
                Text = caption,
                StartPosition = FormStartPosition.CenterScreen               
            };
            Label textLabel = new Label() { Left = 10, Top = 20, Text = text, AutoSize = true };
            TextBox textBox = new TextBox() { Left = 10, Top = 50, Width = 480 };
            Button confirmation = new Button() { Text = "Ok", Left = 415, Width = 75, Height = 23, Top = 75, DialogResult = DialogResult.OK };
            confirmation.Click += (sender, e) => { prompt.Close(); };
            prompt.Controls.Add(textBox);
            prompt.Controls.Add(confirmation);
            prompt.Controls.Add(textLabel);
            prompt.AcceptButton = confirmation;

            return prompt.ShowDialog() == DialogResult.OK ? textBox.Text : "";

        }
    }
}
