using System;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using ImageMagick;
using License;
using ScanHelper.Functions;

//todo Przy wyborze rodzaju wyświetlanego pliku możnabyłoby ustawić tak, że klikając lewym przyciskiem myszy mamy nazwany dokument, który należy tylko do jednej grupy dokumentów, a klikając prawym przyciskiem ten dokument mógłby się kopiować, żeby można było go przypisać do drugiej grupy dokumentów

namespace ScanHelper
{
    public partial class FrmMain : Form
    {
        private readonly DataSet _dsJpgFiles = new DataSet();
        private readonly DataSet _dsDictionary = new DataSet();

        private int _idActiveJpg;
        private int _filesCounter;
        private int _filesSkipped;

        private bool _autoZnak;
        private string _powiat;

        private readonly int[] _btnDictionary = new int[100];

        private byte[] _certPublicKeyData;

        private MyLicense _license;

        private Image _activeImage;

        private bool _dragging;
        private int _xPos;
        private int _yPos;

        private int _zoom = 0;

        public FrmMain()
        {
            InitializeComponent();

            InitializeCustom();
        }

        private void InitializeCustom()
        {
            Icon = Resources.ScanHelper;

            btnOpenDirectory.Text = @"Wskaż folder";
            btnOpenFiles.Text = @"Wskaż pliki";

            btnBack.Text = @"Cofnij";
            btnRotate.Text = @"Obróć";
            btnSkip.Text = @"Pomiń";
            btnScalAuto.Text = @"Scal pliki";
            btnZnakWodny.Text = @"Znak wodny";

            statusStripMainLabel.Text = @"Aktualny plik JPG: 0/0";

            _powiat = Functions.IniParser.ReadIni("powiat", "nazwa");

            textBoxOperat.Text = Functions.IniParser.ReadIni("Operat", "RecentOperat");

            if (string.IsNullOrEmpty(Functions.IniParser.ReadIni("settings", "autoznak")))
            {
                Functions.IniParser.SaveIni("settings", "autoznak", checkBoxZnakWodny.Checked.ToString());
                _autoZnak = false;
                checkBoxZnakWodny.Checked = false;
                checkBoxZnakWodny.Enabled = false;
            }
            else
            {
                _autoZnak = Convert.ToBoolean(Functions.IniParser.ReadIni("settings", "autoznak"));
                checkBoxZnakWodny.Checked = _autoZnak;
                checkBoxZnakWodny.Enabled = false;
            }

            btnZnakWodny.Enabled = false;

            // ustawienie atrybutów początkowych dla przycisków wyboru rodzaju pliku
            foreach (Button btn in groupBoxButtons.Controls.OfType<Button>())
            {
                btn.Enabled = false;
                btn.Text = @"brak";
            }

            // dodanie tabeli z listą plików do przetworzenia
            _dsJpgFiles.DataSetName = "dsJPGFiles";

            DataTable jpgFiles = new DataTable("JPGFiles");
            jpgFiles.Columns.Add("Id", typeof(int));
            jpgFiles.Columns.Add("PathAndFileName", typeof(string));
            jpgFiles.Columns.Add("FileName", typeof(string));
            jpgFiles.Columns.Add("Path", typeof(string));
            jpgFiles.Columns.Add("FileNameNew", typeof(string));
            jpgFiles.Columns.Add("Prefix", typeof(string));
            jpgFiles.Columns.Add("PrefixCode", typeof(int));
            _dsJpgFiles.Tables.Add(jpgFiles);

            // dodanie tabeli z konfiguracją słowników i ilością plików danego rodzaju
            DataTable dtDictionary = new DataTable("Dictionary");
            dtDictionary.Columns.Add("ID_RODZ_DOK", typeof(string));
            dtDictionary.Columns.Add("OPIS", typeof(string));
            dtDictionary.Columns.Add("PREFIX", typeof(string));
            dtDictionary.Columns.Add("SCAL", typeof(string));
            _dsDictionary.Tables.Add(dtDictionary);

            // wczytanie słownika rodzajów plików i utworzenie na podstawie niego przycisków
            _dsDictionary.ReadXml(@"Dictionary.xml");

            for (int buttonIndex = 1; buttonIndex <= _dsDictionary.Tables["Dictionary"].Rows.Count; buttonIndex++)
            {
                groupBoxButtons.Controls["btnDictionary" + buttonIndex].Text = _dsDictionary.Tables["Dictionary"].Rows[buttonIndex - 1]["OPIS"].ToString();
            }

            // ustawieie liczników dokumentów
            for (int i = 0; i < _btnDictionary.Length; i++) _btnDictionary[i] = 0;

            pictureBoxView.MouseWheel += PictureBoxViewOnMouseWheel;
            panelView.MouseWheel += PictureBoxViewOnMouseWheel;
        }

        private void PictureBoxViewOnMouseWheel(object sender, MouseEventArgs e)
        {
            int wheelValue = e.Delta / 50;

            _zoom += wheelValue;

            if (_zoom < 0)
                _zoom = 0;

            if (_zoom > 100)
                _zoom = 100;

            if (_zoom >= 0 && _zoom <= 100)
            {
                pictureBoxView.Image = ImageZoom(_activeImage, _zoom);
                trackBarZoom.Value = _zoom;
            }
        }

        private void FrmMain_Load(object sender, EventArgs e)
        {
            string msg = string.Empty;
            LicenseStatus status = LicenseStatus.Undefined;

            Assembly assembly = Assembly.GetExecutingAssembly();

            using (MemoryStream memoryStream = new MemoryStream())
            {
                assembly.GetManifestResourceStream("ScanHelper.LicenseVerify.cer")?.CopyTo(memoryStream);

                _certPublicKeyData = memoryStream.ToArray();
            }

            if (File.Exists("license.lic"))
            {
                _license = (MyLicense)LicenseHandler.ReadLicense(typeof(MyLicense), File.ReadAllText("license.lic"), _certPublicKeyData, out status, out msg);
            }
            else
            {
                FormLicense frm = new FormLicense();
                frm.ShowDialog(this);
            }

            switch (status)
            {
                case LicenseStatus.Undefined:

                    MessageBox.Show(@"By używać tej aplikacji musisz posiadać aktualną licencję", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Application.Exit();
                    break;

                case LicenseStatus.Invalid:
                case LicenseStatus.Cracked:
                case LicenseStatus.Expired:

                    MessageBox.Show(msg, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Application.Exit();
                    break;

                case LicenseStatus.Valid:

                    string licenseOwner = _license?.LicenseOwner;

                    statusStripLicense.Text = $"Licencja: {_license?.Type}. Licencja ważna do: {_license?.LicenseEnd}";

                    Text = Application.ProductName + ' ' + Application.ProductVersion + @" - " + licenseOwner?.Split('\n').First();

                    Location = new Point(Convert.ToInt32(Functions.IniParser.ReadIni("FormMain", "X")), Convert.ToInt32(Functions.IniParser.ReadIni("FormMain", "Y")));

                    using (FileStream stream = new FileStream(AppDomain.CurrentDomain.BaseDirectory + @"ScanHelper.jpg", FileMode.Open, FileAccess.Read))
                    {
                        _activeImage = Image.FromStream(stream);
                    }

                    pictureBoxView.Image = ImageZoom(_activeImage, 0);
                    pictureBoxView.Left = 0;
                    pictureBoxView.Top = 0;
                    pictureBoxView.Location = new Point((panelView.ClientSize.Width / 2) - (pictureBoxView.Image.Width / 2), (panelView.ClientSize.Height / 2) - (pictureBoxView.Height / 2));

                    trackBarZoom.Value = 0;

                    _zoom = 0;
                    

                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        // funkcja wywołująca okno z konfiguracją
        private void MnuMainKonfiguracja_Click(object sender, EventArgs e)
        {
            // wywołanie okna z konfiguracją programu
            using (FrmKonfiguracja frm = new FrmKonfiguracja())
            {
                frm.ShowDialog(this);
            }
        }

        // funkjca wyboru plików jpg z dysku
        private void BtnOpenJPG_Click(object sender, EventArgs e)
        {
            string folderName = string.Empty;
            string[] fileNames = { };

            DialogResult result;

            string buttonName = ((Button) sender).Name;

            switch (buttonName)
            {
                case "btnOpenFiles":

                    OpenFileDialog ofDialog = new OpenFileDialog
                    {
                        Filter = "JPG (*.jpg)|*.jpg",
                        Multiselect = true,
                        InitialDirectory = Functions.IniParser.ReadIni("Files", "LastDirectory")
                    };

                    result = ofDialog.ShowDialog();

                    if (result == DialogResult.OK)
                    {
                        folderName = Path.GetDirectoryName(ofDialog.FileName);
                        fileNames = ofDialog.FileNames;
                        Array.Sort(fileNames, new NaturalStringComparer());

                        Functions.IniParser.SaveIni("Files", "LastDirectory", folderName);
                    }
                    else return;

                    break;

                case "btnOpenDirectory":
                
                    FolderBrowserDialog fbdOpen = new FolderBrowserDialog
                    {
                        ShowNewFolderButton = false,
                        SelectedPath = Functions.IniParser.ReadIni("Files", "LastDirectory")
                    };

                    result = fbdOpen.ShowDialog();

                    if (result == DialogResult.OK)
                    {
                        folderName = fbdOpen.SelectedPath;
                        fileNames = Directory.GetFiles(folderName, "*.jpg",SearchOption.TopDirectoryOnly);
                        Array.Sort(fileNames, new NaturalStringComparer());

                        Functions.IniParser.SaveIni("Files", "LastDirectory", folderName);

                        if (fileNames.Length == 0)
                        {
                            using (FileStream stream = new FileStream(AppDomain.CurrentDomain.BaseDirectory + @"ScanHelper.jpg", FileMode.Open, FileAccess.Read))
                            {
                                _activeImage = Image.FromStream(stream);
                            }

                            pictureBoxView.Image = ImageZoom(_activeImage, 0);
                            pictureBoxView.Left = 0;
                            pictureBoxView.Top = 0;
                            pictureBoxView.Location = new Point((panelView.ClientSize.Width / 2) - (pictureBoxView.Image.Width / 2), (panelView.ClientSize.Height / 2) - (pictureBoxView.Height / 2));

                            trackBarZoom.Value = 0;

                            _zoom = 0;

                            return;
                        }

                    } else return;

                    break;
            }

            listBoxFiles.Items.Clear();
            
            _dsJpgFiles.Tables["JPGFiles"].Clear();
            _filesCounter = 0;
            _filesSkipped = 0;

            int id = 0;
            foreach (string pathAndFileName in fileNames)
            {
                DataRow row = _dsJpgFiles.Tables["JPGFiles"].NewRow();
                row["Id"] = id++;
                row["PathAndFileName"] = pathAndFileName;
                row["FileName"] = pathAndFileName.Substring(pathAndFileName.LastIndexOf('\\') + 1);
                row["Path"] = pathAndFileName.Substring(0, pathAndFileName.LastIndexOf('\\'));
                _dsJpgFiles.Tables["JPGFiles"].Rows.Add(row);

                listBoxFiles.Items.Add(row["FileName"]);
            }

            _dsJpgFiles.Tables["JPGFiles"].WriteXml("JPGFiles.xml");

            // wyświetl pierwszy z plików z listy wskazanych
            _idActiveJpg = 0;

            DataRow[] rJpgFiles = _dsJpgFiles.Tables["JPGFiles"].Select("Id = '" + _idActiveJpg + "'");

            using (FileStream stream = new FileStream(rJpgFiles[0]["PathAndFileName"].ToString(), FileMode.Open, FileAccess.Read))
            {
                _activeImage = Image.FromStream(stream);
            }

            pictureBoxView.Image = ImageZoom(_activeImage, 0);
            pictureBoxView.Left = 0;
            pictureBoxView.Top = 0;
            pictureBoxView.Location = new Point((panelView.ClientSize.Width / 2) - (pictureBoxView.Image.Width / 2), (panelView.ClientSize.Height / 2) - (pictureBoxView.Height / 2));

            trackBarZoom.Value = 0;

            _zoom = 0;

            listBoxFiles.SetSelected(_idActiveJpg, true);

            long fileSize = new FileInfo(rJpgFiles[0]["PathAndFileName"].ToString()).Length / 1024;

            statusStripMainLabel.Text = $"Aktualny plik JPG: {(Convert.ToInt16(rJpgFiles[0]["Id"]) + 1)}/{_dsJpgFiles.Tables["JPGFiles"].Rows.Count} - {rJpgFiles[0]["PathAndFileName"]} [{fileSize} KB]";

            // uaktywnij przyciski, które mają wartości
            foreach (Button btn in groupBoxButtons.Controls.OfType<Button>())
            {
                if (btn.Text != @"brak") btn.Enabled = true;
            }

            for (int i = 0; i < _btnDictionary.Length; i++) _btnDictionary[i] = 0;

            textBoxOperat.Text = folderName?.Split(Path.DirectorySeparatorChar).Last();

            btnZnakWodny.Enabled = true;

        }

        private void BtnDictionary_Click(object sender, EventArgs e)
        {

            if (_filesCounter == _dsJpgFiles.Tables["JPGFiles"].Rows.Count)
            {
                MessageBox.Show(@"Wszystkie dokumenty zindeksowane", ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (listBoxFiles.SelectedIndex != _filesCounter + _filesSkipped)
            {
                MessageBox.Show(@"Wybierz pierwszy niezindeksowany plik na liście", ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string opis = ((Button)sender).Text;

            DataRow[] rDictionary = _dsDictionary.Tables["Dictionary"].Select("OPIS = '" + opis + "'");

            string prefix = rDictionary[0]["PREFIX"].ToString();

            // pobierz nazwę aktualnie wyświetlanego pliku
            DataRow[] rJpgFiles = _dsJpgFiles.Tables["JPGFiles"].Select("Id = '" + _idActiveJpg + "'");

            string pathAndFileName = rJpgFiles[0]["PathAndFileName"].ToString();
            string path = rJpgFiles[0]["Path"].ToString();

            // licznik plików danego typu
            int btnDictionaryCounter = Int16.Parse(((Button)sender).Name.Replace("btnDictionary", ""));
            _btnDictionary[btnDictionaryCounter] = _btnDictionary[btnDictionaryCounter] + 1;

            _filesCounter++;

            string filesCounter = _filesCounter.ToString().PadLeft(3, '0');

            string fileNamenew;

            switch (_powiat)
            {
                case "gdansk":
                    fileNamenew = textBoxOperat.Text + "_" + filesCounter + prefix.Replace(".", string.Empty) + "_" + _btnDictionary[btnDictionaryCounter] + ".jpg";
                    break;

                case "kwidzyn":
                    fileNamenew = filesCounter + "_" + textBoxOperat.Text + "_" + _btnDictionary[btnDictionaryCounter] + prefix + "jpg";
                    break;

                case "kartuzy":
                    fileNamenew = textBoxOperat.Text + "_" + filesCounter + prefix  + "jpg";
                    break;

                default:
                    throw new Exception("Nieznany powiat!");
            }

            rJpgFiles[0]["FileNameNew"] = fileNamenew;
            rJpgFiles[0]["Prefix"] = prefix;
            rJpgFiles[0]["PrefixCode"] = btnDictionaryCounter;

            listBoxFiles.Items[_idActiveJpg] = "OK -> " + fileNamenew;

            // -------------------------------------------------------------
            //  Utwórz katalog wynikowy jeśli go nie było
            // -------------------------------------------------------------
            DirectoryInfo dir = new DirectoryInfo(path + "\\" + textBoxOperat.Text);

            if (!dir.Exists)
            {
                Directory.CreateDirectory(path + "\\" + textBoxOperat.Text);
            }
            // -------------------------------------------------------------

            // skopiuj plik pod nową nazwą do katalogu wynikowego
            File.Copy(pathAndFileName, path + "\\" + textBoxOperat.Text + "\\" + fileNamenew);

            if (_autoZnak)
            {
                SetZnakWodny();
            }

            // -----------------------------
            // załaduj następny plik do okna
            if (++_idActiveJpg < _dsJpgFiles.Tables["JPGFiles"].Rows.Count)
            {
                rJpgFiles = _dsJpgFiles.Tables["JPGFiles"].Select("Id = '" + _idActiveJpg + "'");

                using (FileStream stream = new FileStream(rJpgFiles[0]["PathAndFileName"].ToString(), FileMode.Open, FileAccess.Read))
                {
                    _activeImage = Image.FromStream(stream);
                }

                pictureBoxView.Image = ImageZoom(_activeImage, 0);
                pictureBoxView.Left = 0;
                pictureBoxView.Top = 0;
                pictureBoxView.Location = new Point((panelView.ClientSize.Width / 2) - (pictureBoxView.Image.Width / 2), (panelView.ClientSize.Height / 2) - (pictureBoxView.Height / 2));

                trackBarZoom.Value = 0;

                _zoom = 0;

                listBoxFiles.SetSelected(_idActiveJpg, true);

                long fileSize = new FileInfo(rJpgFiles[0]["PathAndFileName"].ToString()).Length / 1024;

                statusStripMainLabel.Text = $"Aktualny plik JPG: {(Convert.ToInt16(rJpgFiles[0]["Id"]) + 1)}/{_dsJpgFiles.Tables["JPGFiles"].Rows.Count} - {rJpgFiles[0]["PathAndFileName"]} [{fileSize} KB]";
            }
            else
            {
                MessageBox.Show(@"Wszystkie dokumenty zindeksowane", ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            // załaduj następny plik do okna
            // -----------------------------
        }

        private void MnuMainOProgramie_Click(object sender, EventArgs e)
        {
            using (FrmAbout frm = new FrmAbout(_license))
            {
                frm.ShowDialog();
            }
        }

        private void MnuMainExit_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void BtnBack_Click(object sender, EventArgs e)
        {
            if (listBoxFiles.SelectedIndex != _filesCounter + _filesSkipped)
            {
                MessageBox.Show(@"Wybierz pierwszy niezindeksowany plik na liście", ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (_idActiveJpg > 0)
            {
                --_idActiveJpg;


                DataRow[] rJpgFiles = _dsJpgFiles.Tables["JPGFiles"].Select("Id = '" + _idActiveJpg + "'");

                string path = rJpgFiles[0]["Path"].ToString();
                string fileNameNew = rJpgFiles[0]["FileNameNew"].ToString();
                int prefixCode = (int)rJpgFiles[0]["PrefixCode"];

                if (prefixCode != 99) // jeśli był SKIP to nie odejmuj
                {
                    --_filesCounter;
                }
                else
                {
                    --_filesSkipped;
                }

                _btnDictionary[prefixCode] = _btnDictionary[prefixCode] - 1;

                File.Delete(path + "\\" + textBoxOperat.Text + "\\" + fileNameNew);

                using (FileStream stream = new FileStream(rJpgFiles[0]["PathAndFileName"].ToString(), FileMode.Open, FileAccess.Read))
                {
                    _activeImage = Image.FromStream(stream);
                }

                pictureBoxView.Image = ImageZoom(_activeImage, 0);
                pictureBoxView.Left = 0;
                pictureBoxView.Top = 0;
                pictureBoxView.Location = new Point((panelView.ClientSize.Width / 2) - (pictureBoxView.Image.Width / 2), (panelView.ClientSize.Height / 2) - (pictureBoxView.Height / 2));

                trackBarZoom.Value = 0;

                _zoom = 0;


                listBoxFiles.SetSelected(_idActiveJpg, true);
                listBoxFiles.Items[_idActiveJpg] = rJpgFiles[0]["FileName"];

                long fileSize = new FileInfo(rJpgFiles[0]["PathAndFileName"].ToString()).Length / 1024;

                statusStripMainLabel.Text = $"Aktualny plik JPG: {(Convert.ToInt16(rJpgFiles[0]["Id"]) + 1)}/{_dsJpgFiles.Tables["JPGFiles"].Rows.Count} - {rJpgFiles[0]["PathAndFileName"]} [{fileSize} KB]";
            }

        }

        private void FrmMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            Functions.IniParser.SaveIni("Operat", "RecentOperat", textBoxOperat.Text);
            Functions.IniParser.SaveIni("FormMain", "X", Location.X.ToString());
            Functions.IniParser.SaveIni("FormMain", "Y", Location.Y.ToString());

            if (pictureBoxView.Image != null)
            {
                pictureBoxView.Dispose();
            }
        }

        private void ListBoxFiles_SelectedIndexChanged(object sender, EventArgs e)
        {
            ListBox a = (ListBox)sender;

            if (a.SelectedIndex >= 0)
            {
                _idActiveJpg = a.SelectedIndex;

                DataRow[] rJpgFiles = _dsJpgFiles.Tables["JPGFiles"].Select("Id = '" + _idActiveJpg + "'");

                using (FileStream stream = new FileStream(rJpgFiles[0]["PathAndFileName"].ToString(), FileMode.Open, FileAccess.Read))
                {
                    _activeImage = Image.FromStream(stream);
                }

                pictureBoxView.Image = ImageZoom(_activeImage, 0);
                pictureBoxView.Left = 0;
                pictureBoxView.Top = 0;
                pictureBoxView.Location = new Point((panelView.ClientSize.Width / 2) - (pictureBoxView.Image.Width / 2), (panelView.ClientSize.Height / 2) - (pictureBoxView.Height / 2));

                trackBarZoom.Value = 0;

                _zoom = 0;


                long fileSize = new FileInfo(rJpgFiles[0]["PathAndFileName"].ToString()).Length / 1024;

                statusStripMainLabel.Text = $"Aktualny plik JPG: {(Convert.ToInt16(rJpgFiles[0]["Id"]) + 1)}/{_dsJpgFiles.Tables["JPGFiles"].Rows.Count} - {rJpgFiles[0]["PathAndFileName"]} [{fileSize} KB]";
            }

        }

        // obsługa przycisków CTRL + Lewy lub Prawy
        private void FrmMain_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right))
            {
                BtnRotate_ClickOrKeyPress(sender, e);
                e.Handled = true;
            }
        }

        // blokada klawiszy strzałek w lewo i prawo dla listy plików, by można było obsłużyć CTRL + strzałki
        private void ListBoxFiles_KeyDown(object sender, KeyEventArgs e)
        {
            if(e.KeyCode == Keys.Right || e.KeyCode == Keys.Left) e.Handled = true;
        }

        private void BtnRotate_ClickOrKeyPress(object sender, EventArgs e)
        {
            DataRow[] rJpgFiles = _dsJpgFiles.Tables["JPGFiles"].Select("Id = '" + _idActiveJpg + "'");

            if (rJpgFiles.Length == 0) return;

            string activeFileName = rJpgFiles[0]["PathAndFileName"].ToString();

            using (MagickImage image = new MagickImage(activeFileName))
            {
                // jeśli obracanie zostało wywołane klawiszem
                if (e.GetType() == typeof(KeyEventArgs))
                {
                    KeyEventArgs arg = (KeyEventArgs)e;

                    switch (arg.KeyData)
                    {
                        case Keys.Control | Keys.Right:
                            image.Rotate(90);
                            break;

                        case Keys.Control | Keys.Left:
                            image.Rotate(270);
                            break;
                    }
                }

                // jeśli obracanie zostało wywołane myszką
                if (e.GetType() == typeof(MouseEventArgs))
                {
                    MouseEventArgs arg = (MouseEventArgs)e;

                    switch (arg.Button)
                    {
                        case MouseButtons.Left:
                            image.Rotate(270);
                            break;

                        case MouseButtons.Right:
                            image.Rotate(90);
                            break;
                    }
                }

                image.Write(activeFileName);
            }

            using (FileStream stream = new FileStream(activeFileName, FileMode.Open, FileAccess.Read))
            {
                _activeImage = Image.FromStream(stream);
            }

            pictureBoxView.Image = ImageZoom(_activeImage, 0);
            pictureBoxView.Left = 0;
            pictureBoxView.Top = 0;
            pictureBoxView.Location = new Point((panelView.ClientSize.Width / 2) - (pictureBoxView.Image.Width / 2), (panelView.ClientSize.Height / 2) - (pictureBoxView.Height / 2));

            trackBarZoom.Value = 0;

            _zoom = 0;

        }

        private void BtnSkip_Click(object sender, EventArgs e)
        {
            if (listBoxFiles.SelectedIndex != _filesCounter + _filesSkipped)
            {
                MessageBox.Show(@"Wybierz pierwszy niezindeksowany plik na liście", ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _filesSkipped++;

            DataRow[] rJpgFiles = _dsJpgFiles.Tables["JPGFiles"].Select("Id = '" + _idActiveJpg + "'");

            string fileNamePath = rJpgFiles[0]["PathAndFileName"].ToString();

            string fileName = "!----" + Path.GetFileName(fileNamePath);

            File.Move(fileNamePath, Path.GetDirectoryName(fileNamePath) + "\\" + fileName);

            // licznik plików danego typu
            _btnDictionary[99] = _btnDictionary[99] + 1;

            rJpgFiles[0]["PathAndFileName"] = Path.GetDirectoryName(fileNamePath) + "\\" + fileName;
            rJpgFiles[0]["FileName"] = fileName;
            rJpgFiles[0]["FileNameNew"] = fileName;
            rJpgFiles[0]["Prefix"] = "skip";
            rJpgFiles[0]["PrefixCode"] = 99;

            listBoxFiles.Items[_idActiveJpg] = "!----" + listBoxFiles.Items[_idActiveJpg] + " -> SKIP";

            // -----------------------------
            // załaduj następny plik do okna
            if (++_idActiveJpg < _dsJpgFiles.Tables["JPGFiles"].Rows.Count)
            {
                rJpgFiles = _dsJpgFiles.Tables["JPGFiles"].Select("Id = '" + _idActiveJpg + "'");

                using (FileStream stream = new FileStream(rJpgFiles[0]["PathAndFileName"].ToString(), FileMode.Open, FileAccess.Read))
                {
                    _activeImage = Image.FromStream(stream);
                }

                pictureBoxView.Image = ImageZoom(_activeImage, 0);
                pictureBoxView.Left = 0;
                pictureBoxView.Top = 0;
                pictureBoxView.Location = new Point((panelView.ClientSize.Width / 2) - (pictureBoxView.Image.Width / 2), (panelView.ClientSize.Height / 2) - (pictureBoxView.Height / 2));

                trackBarZoom.Value = 0;

                _zoom = 0;

                listBoxFiles.SetSelected(_idActiveJpg, true);

                long fileSize = new FileInfo(rJpgFiles[0]["PathAndFileName"].ToString()).Length / 1024;

                statusStripMainLabel.Text = $"Aktualny plik JPG: {(Convert.ToInt16(rJpgFiles[0]["Id"]) + 1)}/{_dsJpgFiles.Tables["JPGFiles"].Rows.Count} - {rJpgFiles[0]["PathAndFileName"]} [{fileSize} KB]";
            }
            else
            {
                MessageBox.Show(@"Wszystkie dokumenty zindeksowane", ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        private Image ImageZoom(Image img, int value)
        {
            value *= 5;

            double heightFactor = (double)(panelView.ClientSize.Height-10) / img.Height;
            double widthtFactor = (double)(panelView.ClientSize.Width-10) / img.Width;

            double scaleFactor = heightFactor > widthtFactor ? widthtFactor : heightFactor;

            int scaleWidthFit = Convert.ToInt32(Math.Floor(img.Width * scaleFactor));
            int scaleHeightFit = Convert.ToInt32(Math.Floor(img.Height * scaleFactor));

            Bitmap bmp = new Bitmap(img, scaleWidthFit + scaleWidthFit * value / 100, scaleHeightFit + scaleHeightFit * value / 100);
            Graphics g = Graphics.FromImage(bmp);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            return bmp;
        }

        private void BtnScalAuto_Click(object sender, EventArgs e)
        {
           
        }

        private void BtnZnakWodny_Click(object sender, EventArgs e)
        {

        }

        private void SetZnakWodny()
        {
            
        }

        private void FrmMain_ResizeEnd(object sender, EventArgs e)
        {
            pictureBoxView.Image = ImageZoom(_activeImage, 0);
            pictureBoxView.Left = 0;
            pictureBoxView.Top = 0;
            pictureBoxView.Location = new Point((panelView.ClientSize.Width / 2) - (pictureBoxView.Image.Width / 2), (panelView.ClientSize.Height / 2) - (pictureBoxView.Height / 2));

            trackBarZoom.Value = 0;

            _zoom = 0;

        }

        private void BtnRotate_Click(object sender, EventArgs e)
        {

        }

        private void TrackBarZoom_Scroll(object sender, EventArgs e)
        {
            pictureBoxView.Image = ImageZoom(_activeImage, trackBarZoom.Value);
        }

        private void PictureBoxView_MouseUp(object sender, MouseEventArgs e)
        {
            _dragging = false;
        }

        private void PictureBoxView_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _xPos = e.X;
                _yPos = e.Y;
            }
        }

        private void PictureBoxView_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragging && sender is Control c)
            {
                c.Top = e.Y + c.Top - _yPos;
                c.Left = e.X + c.Left - _xPos;
            }
        }

        private void PictureBoxView_DoubleClick(object sender, EventArgs e)
        {
            pictureBoxView.Image = ImageZoom(_activeImage, 0);
            pictureBoxView.Left = 0;
            pictureBoxView.Top = 0;
            pictureBoxView.Location = new Point((panelView.ClientSize.Width / 2) - (pictureBoxView.Image.Width / 2), (panelView.ClientSize.Height / 2) - (pictureBoxView.Height / 2));

            trackBarZoom.Value = 0;

            _zoom = 0;
        }

        private void PanelView_DoubleClick(object sender, EventArgs e)
        {
            pictureBoxView.Image = ImageZoom(_activeImage, 0);
            pictureBoxView.Left = 0;
            pictureBoxView.Top = 0;
            pictureBoxView.Location = new Point((panelView.ClientSize.Width / 2) - (pictureBoxView.Image.Width / 2), (panelView.ClientSize.Height / 2) - (pictureBoxView.Height / 2));

            trackBarZoom.Value = 0;

            _zoom = 0;
        }
    }

}