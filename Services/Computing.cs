using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using WebToolNet.UtilExtension;

namespace WebDaySurgery.Services
{
    // 從公司 HIS (.NET Framework 版) 的 HIS2BusinessRule 原樣複製過來的臨床計算公式 (eGFR、UPCR、UACR…)。
    // 命名、寫法都刻意不動，才能直接跟 HIS 原始碼比對；HIS 那邊改了公式這份不會跟著變，要自己同步。
    // 只有這個網站在用，所以放專案裡不放 WebToolNet
    public class Computing
    {

        public Computing()
        {

        }

        /// <summary>
        /// 傳入身高.體重,回傳BMI
        /// </summary>
        /// <param name="bodyHeight">身高</param>
        /// <param name="bodyWeigth">體重</param>
        public double getBMI(double bodyHeight, double bodyWeigth)
        {
            //體重除以身高(以公尺為單位)的2次方
            return bodyWeigth / Math.Pow((bodyHeight / 100), 2);
        }


        /// <summary>
        /// 傳入身高.體重，傳回BSA值
        /// </summary>
        /// <param name="bodyHeight">身高</param>
        /// <param name="bodyWeigth">體重</param>
        /// <returns></returns>
        public double getBSA(double bodyHeight, double bodyWeigth)
        {
            return Math.Sqrt(bodyHeight * bodyWeigth / 3600);

        }


        public double geteGFR(string CREA, int Age, string Sex) {

            string val = CREA;
            val = val.Replace(">", "").Replace("<", "");
            double dVal = val.pToDouble();

            return geteGFR(dVal, Age, Sex);

        }

        public double geteGFRCKDEPI(string CREA, int Age, string Sex)
        {

            string val = CREA;
            val = val.Replace(">", "").Replace("<", "");
            double dVal = val.pToDouble();

            return geteGFRCKDEPI(dVal, Age, Sex);

        }

        /// <summary>
        /// 傳入CREA.年齡.姓別,傳回eGFR值
        /// </summary>
        /// <param name="CREA">Creatinine,又簡稱Cr</param>
        /// <param name="Age">年齡</param>
        /// <param name="Sex">姓別(1為男,0為女)</param>
        public double geteGFR(double CREA, int Age, string Sex)
        {

            // 男性：186 × (血清肌酸酐)^-1.154 × (年齡)^-0.203次方
            // 女性：186 × (血清肌酸酐)^-1.154 × (年齡)^-0.203次方 × 0.742
            if (Sex == "1") {
                return 186 * Math.Pow(CREA, -1.154) * Math.Pow(Age, -0.203);
            } else {
                return 186 * Math.Pow(CREA, -1.154) * Math.Pow(Age, -0.203) * 0.742;
            }

        }

        /// <summary>
        /// 傳入CREA.年齡.姓別,傳回eGFR(CKD-EPI)值
        /// </summary>
        /// <param name="CREA">Creatinine,又簡稱Cr</param>
        /// <param name="Age">年齡</param>
        /// <param name="Sex">姓別(1為男,0為女)</param>
        public double geteGFRCKDEPI(double CREA, int Age, string Sex)
        {
            //GFR = 142 * min(Scr / κ, 1)α* max(Scr/ κ, 1)-1.200 * 0.9938Age * 1.012[if female]

            //Scr：血清肌酐濃度（mg / dL）
            //κ：0.7（女性）或 0.9（男性）
            //α：-0.241（女性）或 -0.302（男性）
            //min：Scr / κ 和 1 之間的最小值(若Scr / κ < = 1, 則取Scr / κ，反之取1)
            //max：Scr / κ 和 1 之間的最大值(若Scr / κ > 1, 則取Scr / κ, 反之取1)
            //Age：年齡（年）
            //if Female：如果患者為女性，則乘以 1.012
            //註解
            //公式中的 min 和 max 表示在計算過程中選擇 Scr / κ 和 1 之間的最小值或最大值。

            // 定義變數
            double kappa = Sex == "0" ? 0.7 : 0.9;
            double alpha = Sex == "0" ? -0.241 : -0.302;
            double femaleFactor = Sex == "0" ? 1.012 : 1.0;
            double dCrea = Math.Round(CREA, 2, MidpointRounding.AwayFromZero);
            double scr_kappa = dCrea / kappa;

            // 取 min(scr/kappa, 1)
            double minPart = Math.Min(scr_kappa, 1);

            // 取 max(scr/kappa, 1)
            double maxPart = Math.Max(scr_kappa, 1);

            // 計算 GFR
            double gfr = 142 * Math.Pow(minPart, alpha)
                             * Math.Pow(maxPart, -1.200)
                             * Math.Pow(0.9938, Age)
                             * femaleFactor;

            return gfr;
        }


        /// <summary>
        /// 傳入體重.CREA.姓別.年齡,傳回Clcr值
        /// </summary>
        /// <param name="bodyWeigth">體重</param>
        /// <param name="CREA">Creatinine,又簡稱Cr</param>
        /// <param name="Sex">姓別(1為男,0為女)</param>
        /// <param name="Age">年齡</param>
        /// <returns></returns>
        public double getClcr(double bodyWeigth, double CREA, string Sex, int Age)
        {
            //男性： Clcr = [(140 - 年齡)x體重] / (72x CREA)
            //女性： Clcr = [0.85x(140 - 年齡)x體重]/ (72x CREA)
            if (Sex == "1") {
                return ((140 - Age) * bodyWeigth) / (72 * CREA);
            } else {
                return 0.85 * ((140 - Age) * bodyWeigth) / (72 * CREA);
            }
        }


        public double getLDLc(double CHOL, double TRIG, double HDLc)
        {
        //總膽固醇─高密度脂蛋白膽固醇─（三酸甘油脂÷5）

            return CHOL - HDLc - (TRIG / 5);

        }


        public double getIBW(double bodyHeight,string Sex)
        {
            double temp=0.0;
            // 男 : (身高 - 170 )* O.6 + 62
            // 女 : (身高 - 158 )* O.5 + 52

            if (Sex == "1") {

              temp= (bodyHeight-170)*0.6+62;
               return temp ;

            } else {

                temp = (bodyHeight - 158) * 0.5 + 52;
                return temp;
            }
        }


        public double getTLC(string  strWBC, string  strLYM)
        {
            double dWBC = strWBC.Replace(">", "").Replace("<", "").pToDouble();
            double dLYM = strLYM.Replace(">", "").Replace("<", "").pToDouble();

            return getTLC(dWBC, dLYM);
        }

        public double getTLC(double WBC, double LYM) {
            double temp = 0.0;

            temp = (WBC * 1000) * (LYM / 100);

            return temp;
        }



        public double getUPCR(string  strUrineTP, string  strCreatinine)
        {
           double durineTP=  strUrineTP.Replace(">", "").Replace("<", "").pToDouble();
           double dcreatinine = strCreatinine.Replace(">", "").Replace("<", "").pToDouble();

            return getUPCR(durineTP, dcreatinine);
        }

        /// <summary>
        /// 計算UPCR值
        /// </summary>
        /// <param name="urineTP"></param>
        /// <param name="creatinine"></param>
        /// <returns></returns>
        public double getUPCR(double urineTP, double creatinine)
        {
            return urineTP / creatinine * 1000;
        }


        public double getUACR(string strMicroalbum, string  strCreatinine)
        {

            double dmicroalbum = strMicroalbum.Replace(">", "").Replace("<", "").pToDouble();
            double dcreatinine = strCreatinine.Replace(">", "").Replace("<", "").pToDouble();

            return getUACR(dmicroalbum, dcreatinine);
        }

        /// <summary>
        /// 計算UACR值
        /// </summary>
        /// <param name="microalbum"></param>
        /// <param name="creatinine"></param>
        /// <returns></returns>
        public double getUACR(double microalbum, double creatinine)
        {
            return (microalbum / creatinine) * 100;
        }


        public double get24HR_Urine_TP(string strUrineTP, string strVolume)
        {


            double durineTP = strUrineTP.Replace(">", "").Replace("<", "").pToDouble();
            double dvolume = strVolume.Replace(">", "").Replace("<", "").pToDouble();

            return get24HR_Urine_TP(durineTP, dvolume);

        }

        /// <summary>
        /// 傳入Urine T.P 及取得 24小時 Urine T.P mg/day
        /// </summary>
        /// <param name="urineTP"></param>
        /// <param name="volume"></param>
        /// <returns></returns>
        public double get24HR_Urine_TP(double urineTP, double volume)
        {

            return (urineTP * volume) / 100;
        }


        public double getTSAT(string strIron, string strTibc)
        {
            double diron = strIron.Replace(">", "").Replace("<", "").pToDouble();
            double dtibc = strTibc.Replace(">", "").Replace("<", "").pToDouble();

            return getTSAT(diron ,dtibc);
        }

        /// <summary>
        /// 傳入IRON及T.I.B.C，來求得TSAT的值
        /// </summary>
        /// <param name="iron"></param>
        /// <param name="tibc"></param>
        /// <returns></returns>
        public double getTSAT(double iron, double tibc)
        {
            return (iron / tibc) * 100;
        }




        /// <summary>
        /// 傳入孕婦的最後一次月經日期(民國年月日)，系統計算出預產期(民國年月日)
        /// </summary>
        /// <param name="strTWD_LMP">最後一次月經日期(民國年月日)</param>
        /// <returns></returns>
        public string getEDC(string strTWD_LMP)
        {

            //最後一次月經日期,月 + 9日 + 7,即為預產期
            //例如:最後一次月經108年2月1號
            //    預產期108年11月8號

            string result = "";
            WebToolNet.Validation.CheckDate checkDate = new WebToolNet.Validation.CheckDate();

            if (checkDate.isTWDate(strTWD_LMP) == true)
            {
                //WebToolNet.Dates.DateComputing dateComputing = new WebToolNet.Dates.DateComputing();
                //dateComputing.
                DateTime dateEDC = strTWD_LMP.pToDateTime().AddMonths(+9).AddDays(+7);
                result = dateEDC.pRyyymmdd();
            }

            return result;
        }



        /// <summary>
        /// 傳入基準日(sBaseDate)，來計算距離預產期(sEDC)的懷孕週數
        /// </summary>
        /// <param name="sBaseDate">傳入的基準日(民國年月日)</param>
        /// <param name="sEDC">傳入預產期(民國年月日)</param>
        /// <returns></returns>
        public string getNumberOfWeeksOfPregnancy(string sBaseDate, string sEDC)
        {
            //傳入基準日(sBaseDate)，來計算距離預產期(sEDC)的懷孕週數
            //若計算結果的週數大於41週，則回傳空字串

            string result = "";
            WebToolNet.Validation.CheckDate checkDate = new WebToolNet.Validation.CheckDate();
            if (checkDate.isTWDate(sBaseDate) == true && checkDate.isTWDate(sEDC) == true)
            {
                //if (sEDC.pToDateTime() > sBaseDate.pToDateTime())
                //{
                    TimeSpan Total = sEDC.pToDateTime().Subtract(sBaseDate.pToDateTime()); //日期相減

                    if (((280 - Total.Days) / 7) <= 41)
                    {
                        result = ((280 - Total.Days) / 7).ToString();
                        if (((280 - Total.Days) % 7) != 0)
                        {
                            result += "+" + ((280 - Total.Days) % 7);
                        }
                    }

                //}

            }



            return result;

        }

    }
}
